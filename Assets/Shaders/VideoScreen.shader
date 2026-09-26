// A video screen for the store's TV room and cinema. Shows _MainTex fitted to the screen with black bars
// (letterbox or pillarbox) from _VideoAspect and _ScreenAspect. The CRT look is optional: barrel curvature,
// scanlines, a phosphor mask, vignette, static and a faint glass glow when the tube is dark.
Shader "PlexBuster/Video Screen"
{
    Properties
    {
        _MainTex ("Picture", 2D) = "black" {}
        _VideoAspect ("Picture Aspect", Float) = 1.7778
        _ScreenAspect ("Screen Aspect", Float) = 1.7778
        _Brightness ("Brightness", Float) = 1
        _Curvature ("Curvature", Range(0, 0.5)) = 0
        _Scanlines ("Scanlines", Range(0, 1)) = 0
        _ScanlineCount ("Scanline Count", Float) = 480
        _Mask ("Phosphor Mask", Range(0, 1)) = 0
        _Vignette ("Vignette", Range(0, 2)) = 0
        _Static ("Static", Range(0, 1)) = 0
        [Toggle] _Fill ("Fill (crop instead of bars)", Float) = 0
        _Glass ("Glass Colour", Color) = (0, 0, 0, 1)
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry" }

        Pass
        {
            Name "Unlit"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);

            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST;
                float _VideoAspect;
                float _ScreenAspect;
                float _Brightness;
                float _Curvature;
                float _Scanlines;
                float _ScanlineCount;
                float _Mask;
                float _Vignette;
                float _Static;
                float _Fill;
                float4 _Glass;
            CBUFFER_END

            Varyings Vert(Attributes input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.uv = input.uv;
                return output;
            }

            float Hash(float2 p)
            {
                p = frac(p * float2(443.897, 441.423));
                p += dot(p, p.yx + 19.19);
                return frac((p.x + p.y) * p.x);
            }

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                // Barrel distortion: the picture bulges like the face of a tube.
                float2 centred = input.uv * 2 - 1;
                centred *= 1 + _Curvature * dot(centred, centred) * float2(0.2, 0.25);
                float2 uv = centred * 0.5 + 0.5;
                float inside = step(0, uv.x) * step(uv.x, 1) * step(0, uv.y) * step(uv.y, 1);

                // Fit the picture: bars top and bottom for wide films, at the sides for narrow ones. Fill crops
                // instead, for films whose file already has bars (they'd show as a small window otherwise).
                float fit = _VideoAspect / _ScreenAspect;
                float2 pictureUv = uv;
                if ((fit > 1) == (_Fill < 0.5)) pictureUv.y = (uv.y - 0.5) * fit + 0.5;
                else pictureUv.x = (uv.x - 0.5) / fit + 0.5;
                float inPicture = step(0, pictureUv.x) * step(pictureUv.x, 1) * step(0, pictureUv.y) * step(pictureUv.y, 1);

                half3 colour = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, pictureUv).rgb * inPicture;

                // Static: snow while the tube has no signal.
                float snow = Hash(floor(uv * float2(320, 240)) + frac(_Time.y * 37.0) * 91.0);
                colour = lerp(colour, snow.xxx * 0.85, _Static);

                // Scanlines and the phosphor mask, both in picture space so they sit still on the glass. Each fades
                // out when its lines get finer than the headset's pixels, which would otherwise shimmer (moire).
                float lines = uv.y * _ScanlineCount;
                float lineDetail = saturate(1.5 - fwidth(lines) * 2.5);
                float scan = sin(lines * PI);
                colour *= lerp(1, 0.55 + 0.45 * scan * scan, _Scanlines * lineDetail);
                float triads = uv.x * _ScanlineCount * _ScreenAspect;
                float triadDetail = saturate(1.5 - fwidth(triads) * 2.5);
                float triad = frac(triads);
                half3 mask = half3(saturate(1.5 - abs(triad * 3 - 0.5) * 1.5),
                                   saturate(1.5 - abs(triad * 3 - 1.5) * 1.5),
                                   saturate(1.5 - abs(triad * 3 - 2.5) * 1.5));
                colour *= lerp(half3(1, 1, 1), 0.45 + mask, _Mask * triadDetail);

                float2 edge = uv * (1 - uv);
                float vignette = saturate(pow(edge.x * edge.y * 16, 0.25 * _Vignette));
                colour *= lerp(1, vignette, saturate(_Vignette));

                colour = colour * _Brightness * inside + _Glass.rgb;
                return half4(colour, 1);
            }
            ENDHLSL
        }
    }
}
