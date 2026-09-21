Shader "FruitFlyJoust/SpectralWing"
{
    Properties
    {
        [HideInInspector] _MainTex ("UV source", 2D) = "white" {}
        _BaseColor ("Membrane tint", Color) = (0.64,0.82,0.92,0.36)
        _VeinColor ("Vein color", Color) = (0.16,0.11,0.08,0.92)
        _Iridescence ("Iridescence", Range(0,1)) = 0.72
        _VeinStrength ("Vein strength", Range(0,1)) = 0.9
    }
    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" }
        LOD 250
        Cull Off
        ZWrite Off
        Blend SrcAlpha OneMinusSrcAlpha

        CGPROGRAM
        #pragma surface surf Standard alpha:fade
        #pragma target 3.0
        sampler2D _MainTex;
        fixed4 _BaseColor, _VeinColor;
        half _Iridescence, _VeinStrength;
        struct Input { float2 uv_MainTex; float3 viewDir; };

        void surf(Input IN, inout SurfaceOutputStandard o)
        {
            float2 uv=saturate(IN.uv_MainTex);
            float edge=1-saturate(abs(dot(normalize(IN.viewDir),float3(0,0,1))));
            float phase=edge*11.2+uv.x*1.6+uv.y*.7;
            float3 spectrum=.5+.5*cos(phase+float3(0,2.094,4.189));

            float vein=0;
            vein=max(vein,1-smoothstep(.010,.027,abs(uv.y-(.18+.20*uv.x))));
            vein=max(vein,1-smoothstep(.010,.027,abs(uv.y-(.39+.13*uv.x))));
            vein=max(vein,1-smoothstep(.010,.027,abs(uv.y-(.61-.09*uv.x))));
            vein=max(vein,1-smoothstep(.010,.027,abs(uv.y-(.82-.19*uv.x))));
            float crossA=abs(uv.x-(.26+.035*sin(uv.y*12)));
            float crossB=abs(uv.x-(.53+.028*sin(uv.y*15+1.2)));
            float crossC=abs(uv.x-(.76+.022*sin(uv.y*11+2.1)));
            vein=max(vein,(1-smoothstep(.009,.024,min(crossA,min(crossB,crossC))))*smoothstep(.08,.2,uv.y)*(1-smoothstep(.84,.97,uv.y)));
            vein=saturate(vein*_VeinStrength);

            float3 membrane=lerp(_BaseColor.rgb,spectrum,_Iridescence*(.28+.58*edge));
            o.Albedo=lerp(membrane,_VeinColor.rgb,vein);
            o.Metallic=.04;
            o.Smoothness=.82;
            o.Emission=spectrum*(_Iridescence*edge*.09);
            o.Alpha=saturate(_BaseColor.a+.22*edge+vein*.48);
        }
        ENDCG
    }
    FallBack "Transparent/Diffuse"
}
