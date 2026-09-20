using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace FruitFlyJoust
{
    [ExecuteAlways]
    public sealed class FlyTackFitter : MonoBehaviour
    {
        [Serializable] public sealed class FitData
        {
            public string format="FruitFlyJoust.FlyTack.v1";
            public Vector3 saddleOffset=new Vector3(0,.025f,-.025f);
            public Vector3 saddleRotation=new Vector3(0,0,0);
            public Vector3 saddleSize=new Vector3(.92f,.34f,.72f);
            public Vector3 armourOffset=new Vector3(0,-.015f,.04f);
            public Vector3 armourRotation=new Vector3(0,0,0);
            public Vector3 armourSize=new Vector3(1.06f,.88f,1.18f);
            public bool showSaddle=true;
            public bool showArmour=true;
            public bool saddle=true;
            public bool saddleLow;
            public bool saddlePolyArt;
            public bool reins;
            public bool reinsHead;
            public bool reinsPolyArt;
            public bool armour=true;
            public bool armourPolyArt;
        }

        public FitData fit=new FitData();
        public string outputFile="Research/fly-tack-fit.json";
        public bool rebuild;
        Transform generated;
        static Material saddleMaterial,armourMaterial;

        void OnEnable(){LoadFit();Rebuild();}
        void OnValidate(){if(rebuild){rebuild=false;Rebuild();}else Apply();}

        public static FlyTackFitter Ensure(Transform biologicalRoot)
        {
            if(!biologicalRoot)return null;
            var fitter=biologicalRoot.GetComponent<FlyTackFitter>();
            if(!fitter)fitter=biologicalRoot.gameObject.AddComponent<FlyTackFitter>();
            return fitter;
        }

        public void Rebuild()
        {
            var old=transform.Find("Fly tack (horse adapted)");
            if(old){if(Application.isPlaying)Destroy(old.gameObject);else DestroyImmediate(old.gameObject);}
            generated=new GameObject("Fly tack (horse adapted)").transform;
            generated.SetParent(transform,false);
            var source=Resources.Load<GameObject>("FlyTack/Horse Realistic");
            if(!source){Debug.LogWarning("Fly tack source model has not imported yet.",this);return;}
            BuildPiece(source,"Saddle",new[]{"Saddle","Saddle Low","Saddle PA","Reins","Reins Head","Reins PA"},SaddleMaterial());
            BuildPiece(source,"Armour",new[]{"Armour","Armour PA"},ArmourMaterial());
            Apply();
        }

        void BuildPiece(GameObject source,string label,string[] rendererNames,Material material)
        {
            var anchor=new GameObject(label+" fit").transform;anchor.SetParent(generated,false);
            var model=Instantiate(source,anchor,false);model.name=label+" source mesh";
            bool found=false;
            foreach(var renderer in model.GetComponentsInChildren<Renderer>(true))
            {
                bool keep=Array.Exists(rendererNames,n=>string.Equals(renderer.gameObject.name,n,StringComparison.OrdinalIgnoreCase));
                renderer.enabled=keep;
                if(keep){renderer.sharedMaterial=material;found=true;}
            }
            if(!found)Debug.LogWarning("No "+label+" renderer was found in the imported horse model.",this);
        }

        public void Apply()
        {
            if(!generated)generated=transform.Find("Fly tack (horse adapted)");
            if(!generated)return;
            SetPartVisibility();
            Bounds thorax=ThoraxBounds();
            Configure(generated.Find("Saddle fit"),fit.showSaddle && (fit.saddle||fit.saddleLow||fit.saddlePolyArt||fit.reins||fit.reinsHead||fit.reinsPolyArt),fit.saddleOffset,fit.saddleRotation,fit.saddleSize,thorax);
            Configure(generated.Find("Armour fit"),fit.showArmour && (fit.armour||fit.armourPolyArt),fit.armourOffset,fit.armourRotation,fit.armourSize,thorax);
        }

        void SetPartVisibility()
        {
            var saddleRoot=generated.Find("Saddle fit");if(saddleRoot)foreach(var renderer in saddleRoot.GetComponentsInChildren<Renderer>(true))
            {
                switch(renderer.gameObject.name)
                {
                    case "Saddle": renderer.enabled=fit.showSaddle&&fit.saddle;break;
                    case "Saddle Low": renderer.enabled=fit.showSaddle&&fit.saddleLow;break;
                    case "Saddle PA": renderer.enabled=fit.showSaddle&&fit.saddlePolyArt;break;
                    case "Reins": renderer.enabled=fit.showSaddle&&fit.reins;break;
                    case "Reins Head": renderer.enabled=fit.showSaddle&&fit.reinsHead;break;
                    case "Reins PA": renderer.enabled=fit.showSaddle&&fit.reinsPolyArt;break;
                    default: renderer.enabled=false;break;
                }
            }
            var armourRoot=generated.Find("Armour fit");if(armourRoot)foreach(var renderer in armourRoot.GetComponentsInChildren<Renderer>(true))
            {
                if(renderer.gameObject.name=="Armour")renderer.enabled=fit.showArmour&&fit.armour;
                else if(renderer.gameObject.name=="Armour PA")renderer.enabled=fit.showArmour&&fit.armourPolyArt;
                else renderer.enabled=false;
            }
        }

        void Configure(Transform anchor,bool visible,Vector3 offset,Vector3 rotation,Vector3 sizeRatio,Bounds thorax)
        {
            if(!anchor)return;anchor.gameObject.SetActive(visible);if(!visible)return;
            anchor.localPosition=thorax.center+Vector3.up*thorax.extents.y+Vector3.Scale(offset,thorax.size);
            anchor.localRotation=Quaternion.Euler(rotation);
            var model=anchor.childCount>0 ? anchor.GetChild(0) : null;if(!model)return;
            model.localPosition=Vector3.zero;model.localRotation=Quaternion.identity;model.localScale=Vector3.one;
            Bounds source=RendererBounds(model,anchor);
            Vector3 wanted=Vector3.Scale(thorax.size,sizeRatio);
            Vector3 scale=new Vector3(wanted.x/Mathf.Max(.0001f,source.size.x),wanted.y/Mathf.Max(.0001f,source.size.y),wanted.z/Mathf.Max(.0001f,source.size.z));
            model.localScale=scale;
            Bounds fitted=RendererBounds(model,anchor);
            model.localPosition-=fitted.center;
        }

        Bounds ThoraxBounds()
        {
            Transform thorax=transform.Find("0/Thorax");
            if(thorax)
            {
                var renderer=thorax.GetComponent<Renderer>();
                if(renderer)return RendererBounds(renderer,transform);
            }
            return new Bounds(Vector3.zero,new Vector3(.7f,.55f,.9f));
        }

        static Bounds RendererBounds(Transform root,Transform relative)
        {
            var renderers=root.GetComponentsInChildren<Renderer>(true);bool any=false;Bounds bounds=new Bounds();
            foreach(var renderer in renderers)if(renderer.enabled)
            {
                Bounds next=RendererBounds(renderer,relative);
                if(!any){bounds=next;any=true;}else bounds.Encapsulate(next);
            }
            return any ? bounds : new Bounds(Vector3.zero,Vector3.one);
        }

        static Bounds RendererBounds(Renderer renderer,Transform relative)
        {
            Bounds world=renderer.bounds;Vector3 c=world.center,e=world.extents;Bounds local=new Bounds();bool first=true;
            for(int x=-1;x<=1;x+=2)for(int y=-1;y<=1;y+=2)for(int z=-1;z<=1;z+=2)
            {
                Vector3 point=relative.InverseTransformPoint(c+Vector3.Scale(e,new Vector3(x,y,z)));
                if(first){local=new Bounds(point,Vector3.zero);first=false;}else local.Encapsulate(point);
            }
            return local;
        }

        public string SaveFit()
        {
            string project=Path.GetDirectoryName(Application.dataPath),path=Path.GetFullPath(Path.Combine(project,outputFile));
            string json=JsonUtility.ToJson(fit,true);Directory.CreateDirectory(Path.GetDirectoryName(path));File.WriteAllText(path,json);
            string resource=Path.Combine(Application.dataPath,"Resources/FlyTack/FlyTackFit.json");Directory.CreateDirectory(Path.GetDirectoryName(resource));File.WriteAllText(resource,json);return path;
        }
        public bool LoadFit()
        {
            string path=Path.GetFullPath(Path.Combine(Path.GetDirectoryName(Application.dataPath),outputFile));
            string json=File.Exists(path) ? File.ReadAllText(path) : null;
            if(string.IsNullOrEmpty(json)){var asset=Resources.Load<TextAsset>("FlyTack/FlyTackFit");if(asset)json=asset.text;}
            if(string.IsNullOrEmpty(json))return false;var saved=JsonUtility.FromJson<FitData>(json);if(saved==null)return false;fit=saved;Apply();return true;
        }

        static Material SaddleMaterial()
        {
            if(!saddleMaterial){saddleMaterial=new Material(Shader.Find("Standard")){name="Fly saddle leather"};saddleMaterial.color=new Color(.18f,.055f,.025f);saddleMaterial.SetFloat("_Metallic",.08f);saddleMaterial.SetFloat("_Glossiness",.36f);}
            return saddleMaterial;
        }
        static Material ArmourMaterial()
        {
            if(!armourMaterial){armourMaterial=new Material(Shader.Find("Standard")){name="Fly armour bronze"};armourMaterial.color=new Color(.3f,.16f,.045f);armourMaterial.SetFloat("_Metallic",.72f);armourMaterial.SetFloat("_Glossiness",.55f);}
            return armourMaterial;
        }
    }
}
