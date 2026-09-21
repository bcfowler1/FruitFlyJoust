Shader "FruitFlyJoust/FlyBodyDepth"
{
    Properties
    {
        _Color ("Biomodel color", Color) = (0.55,0.28,0.08,1)
        _OcclusionStrength ("Ambient depth", Range(0,1)) = 0.55
        _TextureStrength ("Surface texture", Range(0,0.3)) = 0.11
        _Smoothness ("Smoothness", Range(0,1)) = 0.28
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" }
        LOD 250
        CGPROGRAM
        #pragma surface surf Standard fullforwardshadows vertex:vert
        #pragma target 3.0
        fixed4 _Color;
        half _OcclusionStrength, _TextureStrength, _Smoothness;
        struct Input { float3 localPos; float3 worldNormal; float3 viewDir; };
        void vert(inout appdata_full v, out Input o)
        {
            UNITY_INITIALIZE_OUTPUT(Input,o);
            o.localPos=v.vertex.xyz;
        }
        void surf(Input IN, inout SurfaceOutputStandard o)
        {
            float3 n=normalize(IN.worldNormal);
            float upward=saturate(dot(n,float3(0,1,0))*.5+.5);
            float underside=lerp(1-_OcclusionStrength*.48,1,upward);
            float broad=.5+.5*sin(IN.localPos.x*11+sin(IN.localPos.z*7))*sin(IN.localPos.y*13-IN.localPos.z*5);
            float fine=.5+.5*sin(IN.localPos.x*31+IN.localPos.y*23)*sin(IN.localPos.z*29-IN.localPos.y*17);
            float textureFactor=lerp(1-_TextureStrength,1+_TextureStrength*.42,broad*.65+fine*.35);
            float facing=saturate(dot(normalize(IN.viewDir),n));
            float rim=pow(1-facing,3)*.055;
            o.Albedo=_Color.rgb*underside*textureFactor;
            o.Metallic=.03;
            o.Smoothness=_Smoothness;
            o.Occlusion=saturate(underside*(.86+.14*broad));
            o.Emission=_Color.rgb*rim;
            o.Alpha=1;
        }
        ENDCG
    }
    FallBack "Standard"
}
