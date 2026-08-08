// A ground-glass lens.
//
// HOW THE MAGNIFICATION ACTUALLY HAPPENS, because it is not in this shader: a second camera
// sits at the player's eye looking the same way, with its field of view divided by the
// magnification, and renders to _LensTex. A narrower field of view over the same screen IS
// magnification. This shader's only job is to put that image on the glass and make it look
// like glass.
//
// Sampling is in SCREEN space, not in the quad's own UV. That is what keeps the magnified
// image lined up with the world behind the lens — the rim of the glass frames the same
// direction you were already looking, so moving the loupe over the pan sweeps the view the
// way a real one does. Sampling by mesh UV would paste a static picture onto a disc.
Shader "Gunsmith/Lens"
{
    Properties
    {
        _LensTex ("Magnified view", 2D) = "black" {}
        _Distortion ("Edge distortion", Range(0, 0.4)) = 0.10
        _Chromatic ("Edge fringing", Range(0, 0.03)) = 0.006
        _RimWidth ("Rim width", Range(0.01, 0.5)) = 0.16
        _RimTint ("Rim highlight", Color) = (0.80, 0.85, 0.95, 1)
        _Tint ("Glass tint", Color) = (0.98, 0.99, 1.0, 1)
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry" }

        Pass
        {
            Name "Lens"
            Cull Off
            ZWrite On

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv         : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv         : TEXCOORD0;
                float4 ndc        : TEXCOORD1;
            };

            TEXTURE2D(_LensTex);
            SAMPLER(sampler_LensTex);

            CBUFFER_START(UnityPerMaterial)
                float  _Distortion;
                float  _Chromatic;
                float  _RimWidth;
                float4 _RimTint;
                float4 _Tint;
            CBUFFER_END

            Varyings vert (Attributes IN)
            {
                Varyings OUT;

                VertexPositionInputs positions = GetVertexPositionInputs(IN.positionOS.xyz);
                OUT.positionCS = positions.positionCS;
                OUT.ndc = positions.positionNDC;
                OUT.uv = IN.uv;

                return OUT;
            }

            half4 frag (Varyings IN) : SV_Target
            {
                // Lens-local coordinates, -1..1 out from the centre of the glass.
                float2 centred = IN.uv * 2.0 - 1.0;
                float radius = length(centred);

                // A round lens in a square quad.
                clip(1.0 - radius);

                float2 screen = IN.ndc.xy / IN.ndc.w;

                // Barrel distortion, growing as the square of the radius so the middle stays
                // honest and only the rim bends. This is the tell that reads as "glass".
                float2 bend = centred * _Distortion * radius * radius * 0.08;
                float2 sampleAt = screen + bend;

                // Chromatic fringing, again only near the rim. Real cheap glass splits colour
                // at the edges and its absence is what makes a lens look like a hole.
                float fringe = _Chromatic * radius * radius;

                half red   = SAMPLE_TEXTURE2D(_LensTex, sampler_LensTex, sampleAt + centred * fringe).r;
                half4 mid  = SAMPLE_TEXTURE2D(_LensTex, sampler_LensTex, sampleAt);
                half blue  = SAMPLE_TEXTURE2D(_LensTex, sampler_LensTex, sampleAt - centred * fringe).b;

                half3 colour = half3(red, mid.g, blue) * _Tint.rgb;

                // The rim: darkened just inside the edge where a lens thickens, with a bright
                // ring of ground glass right at the boundary.
                float rim = smoothstep(1.0 - _RimWidth, 1.0, radius);

                colour = lerp(colour, colour * 0.5, rim * 0.7);
                colour += _RimTint.rgb * pow(rim, 4.0) * 0.9;

                return half4(colour, 1.0);
            }
            ENDHLSL
        }
    }

    Fallback Off
}
