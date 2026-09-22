using UnityEngine;
using UnityEngine.Rendering;

namespace FruitFlyJoust
{
    public sealed class FlyFood : MonoBehaviour
    {
        public float nutrition=.65f;
        public float respawnDelay=35;
        [Range(.01f,.95f)] public float remainingConsumedPerSecond=.3f;
        float respawnAt;
        float remaining=1;
        Vector3 fullScale;
        Renderer appearance;
        Collider volume;
        void Awake()
        {
            appearance=GetComponent<Renderer>();volume=GetComponent<Collider>();if(volume)volume.isTrigger=true;
            fullScale=transform.localScale;ShapeGelatinBlob();ApplyScale();
        }
        void ShapeGelatinBlob()
        {
            var filter=GetComponent<MeshFilter>();
            if(filter && filter.sharedMesh)
            {
                var mesh=Instantiate(filter.sharedMesh);mesh.name="Bottom-heavy gelatin food blob";
                var vertices=mesh.vertices;
                for(int i=0;i<vertices.Length;i++)
                {
                    Vector3 vertex=vertices[i];float vertical=Mathf.InverseLerp(-.5f,.5f,vertex.y);
                    // Broaden and flatten the lower mass while tapering the crown.
                    float girth=Mathf.Lerp(1.28f,.72f,vertical);
                    float irregularity=1+Mathf.Sin((vertex.x*7+vertex.z*11)*Mathf.PI)*.035f;
                    vertex.x*=girth*irregularity;vertex.z*=girth/irregularity;vertex.y*=.72f;
                    if(vertex.y<-.27f)vertex.y=Mathf.Lerp(-.36f,vertex.y,.28f);
                    vertices[i]=vertex;
                }
                mesh.vertices=vertices;mesh.RecalculateNormals();mesh.RecalculateBounds();filter.sharedMesh=mesh;
            }
            if(!appearance)return;
            var shader=Shader.Find("Standard");if(!shader)return;
            var material=new Material(shader){name="Translucent gelatin food",color=new Color(1f,.38f,.06f,.58f)};
            material.SetFloat("_Mode",3);material.SetInt("_SrcBlend",(int)BlendMode.SrcAlpha);
            material.SetInt("_DstBlend",(int)BlendMode.OneMinusSrcAlpha);material.SetInt("_ZWrite",0);
            material.DisableKeyword("_ALPHATEST_ON");material.EnableKeyword("_ALPHABLEND_ON");material.DisableKeyword("_ALPHAPREMULTIPLY_ON");
            material.SetFloat("_Glossiness",.88f);material.SetFloat("_Metallic",.03f);
            material.SetColor("_EmissionColor",new Color(.12f,.018f,0));material.EnableKeyword("_EMISSION");material.renderQueue=3000;
            appearance.sharedMaterial=material;appearance.shadowCastingMode=ShadowCastingMode.On;appearance.receiveShadows=true;
        }
        public bool Available { get { return appearance && appearance.enabled && remaining>.01f; } }
        public float RemainingFraction { get { return remaining; } }
        public Vector3 ClosestPoint(Vector3 position)
        {
            return volume && volume.enabled ? volume.ClosestPoint(position) : transform.position;
        }
        public float Consume(float seconds)
        {
            if(!Available || seconds<=0)return 0;
            float fraction=1-Mathf.Pow(1-remainingConsumedPerSecond,seconds);
            float consumed=remaining*fraction;remaining=Mathf.Max(0,remaining-consumed);ApplyScale();
            if(remaining<=.01f)Deplete();return nutrition*consumed;
        }
        public void Eat()
        {
            if(!Available)return;remaining=0;ApplyScale();Deplete();
        }
        void ApplyScale(){if(fullScale==Vector3.zero)fullScale=transform.localScale;transform.localScale=fullScale*remaining;}
        void Deplete(){if(appearance)appearance.enabled=false;if(volume)volume.enabled=false;respawnAt=Time.time+respawnDelay;}
        void Update()
        {
            if(!Available && Time.time>=respawnAt){remaining=1;ApplyScale();appearance.enabled=true;if(volume)volume.enabled=true;}
        }
    }
}
