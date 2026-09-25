using UnityEngine;

/// <summary>Paired visible headlights shared by traffic and delivery vehicles.</summary>
[DisallowMultipleComponent]
public class VehicleHeadlights : MonoBehaviour
{
    Light[] lamps;
    Material lensMaterial;
    void Start()
    {
        var renderers = GetComponentsInChildren<Renderer>();
        if (renderers.Length == 0) return;
        Bounds bounds = new Bounds();
        bool found = false;
        foreach (var renderer in renderers)
        {
            Bounds b = renderer.localBounds;
            for (int i=0;i<8;i++)
            {
                Vector3 offset=Vector3.Scale(b.extents,new Vector3((i&1)==0?-1:1,(i&2)==0?-1:1,(i&4)==0?-1:1));
                Vector3 p=transform.InverseTransformPoint(renderer.transform.TransformPoint(b.center+offset));
                if (!found) { bounds=new Bounds(p,Vector3.zero); found=true; } else bounds.Encapsulate(p);
            }
        }
        Shader shader=Shader.Find("Universal Render Pipeline/Lit");
        if(shader!=null) {
            lensMaterial=new Material(shader);
            lensMaterial.SetColor("_BaseColor",new Color(0.85f,0.82f,0.65f));
            lensMaterial.EnableKeyword("_EMISSION");
        }
        lamps=new Light[2];
        for(int i=0;i<2;i++) {
            var go=new GameObject(i==0?"Left Headlight":"Right Headlight");
            go.transform.SetParent(transform,false);
            go.transform.localPosition=new Vector3(bounds.center.x+(i==0?-1:1)*bounds.extents.x*0.65f,bounds.min.y+bounds.size.y*0.35f,bounds.max.z);
            go.transform.localRotation=Quaternion.LookRotation(new Vector3(0,-0.35f,1));
            Light lamp=go.AddComponent<Light>();
            lamp.type=LightType.Spot; lamp.color=new Color(1,0.94f,0.78f);
            lamp.range=18; lamp.spotAngle=58; lamp.innerSpotAngle=36;
            lamp.shadows=LightShadows.None; lamps[i]=lamp;
            if(lensMaterial==null) continue;
            var lens=GameObject.CreatePrimitive(PrimitiveType.Cube);
            lens.name="Headlight Lens";
            var collider=lens.GetComponent<Collider>(); collider.enabled=false; Destroy(collider);
            lens.transform.SetParent(transform,false);
            lens.transform.localPosition=go.transform.localPosition;
            lens.transform.localScale=new Vector3(bounds.size.x*0.16f,bounds.size.y*0.1f,0.1f);
            var r=lens.GetComponent<Renderer>(); r.sharedMaterial=lensMaterial;
            r.shadowCastingMode=UnityEngine.Rendering.ShadowCastingMode.Off; r.receiveShadows=false;
        }
        Update();
    }
    void Update()
    {
        if(lamps==null) return;
        float hour=GameTimeManager.Instance!=null?GameTimeManager.Instance.CurrentMinutes/60f:12f;
        float darkness=1-Mathf.SmoothStep(0,1,Mathf.InverseLerp(6,9,hour))*(1-Mathf.SmoothStep(0,1,Mathf.InverseLerp(17,20,hour)));
        float strength=Mathf.SmoothStep(0,1,Mathf.InverseLerp(0.2f,0.75f,darkness));
        foreach(var lamp in lamps) { lamp.enabled=strength>0.01f; lamp.intensity=12*strength; }
        if(lensMaterial!=null) lensMaterial.SetColor("_EmissionColor",new Color(1,0.94f,0.78f)*5*strength);
    }
    void OnDestroy() { if(lensMaterial!=null) Destroy(lensMaterial); }
}
