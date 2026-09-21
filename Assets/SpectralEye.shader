Shader "FruitFlyJoust/SpectralEye"
{
    Properties
    {
        _BaseColor ("Eye red", Color) = (0.48,0.018,0.012,1)
        _SheenColor ("Spectral orange", Color) = (1,0.28,0.015,1)
        _SheenStrength ("Sheen strength", Range(0,1)) = 0.7
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" }
        LOD 250
        CGPROGRAM
        #pragma surface surf Standard
        #pragma target 3.0
        fixed4 _BaseColor, _SheenColor;
        half _SheenStrength;
        struct Input { float3 viewDir; float3 worldPos; };
        void surf(Input IN, inout SurfaceOutputStandard o)
        {
            float facing=saturate(abs(dot(normalize(IN.viewDir),o.Normal)));
            float grazing=1-facing;
            float spectralBand=.5+.5*sin(grazing*17.5+IN.worldPos.y*3.1);
            float sheen=saturate((.18+grazing*.82)*lerp(.68,1,spectralBand)*_SheenStrength);
            o.Albedo=lerp(_BaseColor.rgb,_SheenColor.rgb,sheen);
            o.Metallic=.18;
            o.Smoothness=.88;
            o.Emission=_SheenColor.rgb*(grazing*spectralBand*.12*_SheenStrength);
            o.Alpha=1;
        }
        ENDCG
    }
    FallBack "Standard"
}
