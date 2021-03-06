using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class EndlessTerrain : MonoBehaviour
{

    const float scale = 3f; // größe von der Map
    const float viewerMoveThreholdForChunkUpdate = 25f; // Damit die Chunks nicht bei jedem Update() erneut berechnet werden
    const float sqrViewerMoveThreholdForChunkUpdate = viewerMoveThreholdForChunkUpdate*viewerMoveThreholdForChunkUpdate;
    public LODInfo[] detailLevels;
    public static float maxViewDst;
    public Transform viewer;
    public Material mapMaterial;

    public static Vector2 viewerPosition;
    Vector2 viewerPositionOld;
    static MapGenerator mapGenerator;
    int chunkSize;
    int chunkVisibleInViewDst;

    Dictionary<Vector2, TerrainChunk> terrainChunkDictionary = new Dictionary<Vector2, TerrainChunk>();
    static List<TerrainChunk> terrainChunksVisibleLastUpdate = new List<TerrainChunk>(); // Die letzten Visible gemachten Chunks

    void Start() {
        mapGenerator = FindObjectOfType<MapGenerator>();

        maxViewDst = detailLevels[detailLevels.Length -1].visibleDstThreshold; //Maximale Sichtweite ist immer der letzte visible Threshhold welcher in dem Array vorhanden ist
        chunkSize = MapGenerator.mapChunkSize -1 ; // weil es eigentlich 240 pro Chunk sind und nicht 241
        chunkVisibleInViewDst = Mathf.RoundToInt(maxViewDst/chunkSize);

        UpdateVisibleChunks();
    }

    void Update() {
        viewerPosition = new Vector2(viewer.position.x, viewer.position.z) / scale ;

        if((viewerPositionOld - viewerPosition).sqrMagnitude > sqrViewerMoveThreholdForChunkUpdate){
            viewerPositionOld = viewerPosition;
            UpdateVisibleChunks();
        } 
    }

    void UpdateVisibleChunks(){

        for(int i= 0; i<terrainChunksVisibleLastUpdate.Count; i++){ //letzte generierte Chunks werden gelöscht
            terrainChunksVisibleLastUpdate[i].SetVisible(false);
        }
        terrainChunksVisibleLastUpdate.Clear();

        int currentChunkCoordX = Mathf.RoundToInt(viewerPosition.x/chunkSize);
        int currentChunkCoordY = Mathf.RoundToInt(viewerPosition.y/chunkSize);

        for(int yOffset = -chunkVisibleInViewDst; yOffset <= chunkVisibleInViewDst; yOffset++){
            for(int xOffset = -chunkVisibleInViewDst; xOffset <= chunkVisibleInViewDst; xOffset++){

                Vector2 viewedChunkCoord = new Vector2(currentChunkCoordX + xOffset, currentChunkCoordY + yOffset);

                if(terrainChunkDictionary.ContainsKey(viewedChunkCoord)){

                    terrainChunkDictionary[viewedChunkCoord].UpdateTerrainChunk();
                } else {
                    terrainChunkDictionary.Add (viewedChunkCoord, new TerrainChunk(viewedChunkCoord, chunkSize, detailLevels, transform, mapMaterial));
                }
            }
        }
    }
    public class TerrainChunk {
        GameObject meshObject;
        Vector2 position;
        Bounds bounds;

        MeshRenderer meshRenderer;
        MeshFilter meshFilter;
        MeshCollider meshCollider;


        LODInfo[] detailLevels;
        LODMesh[] lodMeshes;
        LODMesh collisonLODMesh;

        MapData mapData;
        bool mapDataReceived;
        int previousLODIndex = -1;

        public TerrainChunk(Vector2 coord, int size, LODInfo[] detailLevels ,Transform parent, Material material) {

            this.detailLevels = detailLevels;

            position = coord * size;
            bounds = new Bounds(position,Vector2.one * size);
            Vector3 positionV3 = new Vector3(position.x,0,position.y);

            meshObject = new GameObject("Terrain Chunk");
            meshRenderer = meshObject.AddComponent<MeshRenderer>();
            meshFilter = meshObject.AddComponent<MeshFilter>();
            meshCollider = meshObject.AddComponent<MeshCollider>();
            meshRenderer.material = material;

            meshObject.transform.position = positionV3 * scale;
            meshObject.transform.parent = parent;
            meshObject.transform.localScale = Vector3.one * scale;

            SetVisible(false);

            lodMeshes = new LODMesh[detailLevels.Length];
            for(int i = 0; i < detailLevels.Length; i++){ // hier werden alle LodMeshes erstellt
                lodMeshes[i] = new LODMesh(detailLevels[i].lod, UpdateTerrainChunk);
                if(detailLevels[i].useForCollider){
                    collisonLODMesh = lodMeshes[i];
                }
            }

            mapGenerator.RequestMapData(position, OnMapDataReceived);
        }

        void OnMapDataReceived(MapData mapData){
            this.mapData = mapData;
            mapDataReceived = true;

            Texture2D texture = TextureGenerator.TextureFromColourMap(mapData.colourMap, MapGenerator.mapChunkSize, MapGenerator.mapChunkSize);
            meshRenderer.material.mainTexture = texture;

            UpdateTerrainChunk();
        }
        

        public void UpdateTerrainChunk(){ // Distance tracking und mesh an und ausschalten, ab welcher Distanze welche Chunks an oder ausgeschalten werden und in welcher Resolution
            if(mapDataReceived){
                float viewerDstFromNearestEdge = Mathf.Sqrt(bounds.SqrDistance(viewerPosition));
                bool visible = viewerDstFromNearestEdge <= maxViewDst;

                if(visible){
                    int lodIndex = 0;
                    for(int i = 0; i < detailLevels.Length-1; i++){
                        if( viewerDstFromNearestEdge > detailLevels[i].visibleDstThreshold){
                            lodIndex = i+1;
                        } else {
                            break;
                        }
                    }
                    if(lodIndex != previousLODIndex){
                        LODMesh lodMesh = lodMeshes[lodIndex];
                        if(lodMesh.hasMesh){
                            previousLODIndex = lodIndex;
                            meshFilter.mesh = lodMesh.mesh;
                        } else if(!lodMesh.hasRequestedMesh){
                            lodMesh.RequestMesh(mapData);
                        }
                    }

                    if(lodIndex == 0){
                        if(collisonLODMesh.hasMesh){
                            meshCollider.sharedMesh = collisonLODMesh.mesh;
                        } else if(!collisonLODMesh.hasRequestedMesh){
                            collisonLODMesh.RequestMesh(mapData);
                        }
                    }

                    terrainChunksVisibleLastUpdate.Add(this);
                }
            SetVisible(visible);
            }  
        }

        public void SetVisible(bool visible){
            meshObject.SetActive(visible);
        }

        public bool isVisible(){
            return meshObject.activeSelf;
        }
    }

    class LODMesh {

        public Mesh mesh ;
        public bool hasRequestedMesh;
        public bool hasMesh;
        int lod;

        System.Action updateCallback;
        public LODMesh(int lod ,System.Action updateCallback){
            this.lod = lod;
            this.updateCallback = updateCallback;
        }

        void OnMeshDataReceived(MeshData meshData){
            mesh = meshData.CreateMesh();
            hasMesh = true;

            updateCallback();
        }

        public void RequestMesh(MapData mapData){
            hasRequestedMesh = true;
            mapGenerator.RequestMeshData(mapData, lod, OnMeshDataReceived);
        }

    }

    [System.Serializable]
    public struct LODInfo {
        public int lod;
        public float visibleDstThreshold; // Sobald der Chunk weiter weg ist von dem Spieler wird es zu einer anderen LevelDetailnummer gewechselt( schlechtere Auflösung)

        public bool useForCollider;
    }
}
