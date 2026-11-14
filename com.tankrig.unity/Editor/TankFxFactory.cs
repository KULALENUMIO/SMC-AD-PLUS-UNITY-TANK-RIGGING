using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace TankRig.Editor
{
    /// <summary>Creates the particle systems and lights at the effect points (no external assets needed).</summary>
    public static class TankFxFactory
    {
        public const string Folder = "Assets/TankRig/Generated";
        static Material smokeMat, additiveMat;

        // ------------------------------------------------------------------ shared assets

        public static void EnsureAssets()
        {
            Directory.CreateDirectory(Folder);
            var smokeTex = EnsureTexture("TankRig_Smoke.png", true);
            var flashTex = EnsureTexture("TankRig_Flash.png", false);
            smokeMat = EnsureMaterial("TankRig_Smoke.mat", smokeTex, false);
            additiveMat = EnsureMaterial("TankRig_Additive.mat", flashTex, true);
        }

        static Texture2D EnsureTexture(string file, bool smoke)
        {
            string path = Folder + "/" + file;
            var existing = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            if (existing) return existing;
            const int size = 64;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float dx = (x + 0.5f) / size * 2f - 1f, dy = (y + 0.5f) / size * 2f - 1f;
                    float r = Mathf.Sqrt(dx * dx + dy * dy);
                    float a = Mathf.Clamp01(1f - r);
                    a = a * a * (3f - 2f * a);
                    if (smoke) a *= 0.7f + 0.3f * Mathf.PerlinNoise(x * 0.18f, y * 0.18f);
                    else a = Mathf.Pow(a, 1.6f);
                    tex.SetPixel(x, y, new Color(1f, 1f, 1f, a));
                }
            File.WriteAllBytes(path, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
            if (AssetImporter.GetAtPath(path) is TextureImporter ti)
            {
                ti.alphaIsTransparency = true;
                ti.wrapMode = TextureWrapMode.Clamp;
                ti.SaveAndReimport();
            }
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        static Material EnsureMaterial(string file, Texture2D tex, bool additive)
        {
            string path = Folder + "/" + file;
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m) return m;
            var shader = Shader.Find("Universal Render Pipeline/Particles/Unlit")
                         ?? Shader.Find("Particles/Standard Unlit")
                         ?? Shader.Find("Sprites/Default");
            m = new Material(shader);
            m.SetTexture("_BaseMap", tex);
            m.SetTexture("_MainTex", tex);
            m.SetColor("_BaseColor", Color.white);
            m.SetFloat("_Surface", 1f);
            m.SetFloat("_Blend", additive ? 2f : 0f);
            m.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            m.SetFloat("_DstBlend", additive ? (float)BlendMode.One : (float)BlendMode.OneMinusSrcAlpha);
            m.SetFloat("_SrcBlendAlpha", (float)BlendMode.One);
            m.SetFloat("_DstBlendAlpha", additive ? (float)BlendMode.One : (float)BlendMode.OneMinusSrcAlpha);
            m.SetFloat("_ZWrite", 0f);
            m.SetOverrideTag("RenderType", "Transparent");
            m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            m.renderQueue = (int)RenderQueue.Transparent;
            AssetDatabase.CreateAsset(m, path);
            return m;
        }

        /// <summary>Low-friction material for the hull colliders, so the tank slides off obstacles instead of sticking.</summary>
        public static PhysicsMaterial HullPhysicsMaterial()
        {
            Directory.CreateDirectory(Folder);
            string path = Folder + "/TankRig_Hull.asset";
            var m = AssetDatabase.LoadAssetAtPath<PhysicsMaterial>(path);
            if (m) return m;
            m = new PhysicsMaterial("TankRig_Hull")
            {
                dynamicFriction = 0.2f,
                staticFriction = 0.2f,
                frictionCombine = PhysicsMaterialCombine.Minimum,
                bounciness = 0f,
            };
            AssetDatabase.CreateAsset(m, path);
            return m;
        }

        public static Material GroundMaterial()
        {
            Directory.CreateDirectory(Folder);
            string path = Folder + "/TankRig_Ground.mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m) return m;
            m = new Material(Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard"));
            var c = new Color(0.36f, 0.38f, 0.32f);
            m.SetColor("_BaseColor", c);
            m.SetColor("_Color", c);
            m.SetFloat("_Smoothness", 0.08f);
            m.SetFloat("_Glossiness", 0.08f);
            AssetDatabase.CreateAsset(m, path);
            return m;
        }

        // ------------------------------------------------------------------ helpers

        static ParticleSystem NewSystem(string name, Transform parent, Material mat, bool worldSpace)
        {
            var go = new GameObject(name);
            Undo.RegisterCreatedObjectUndo(go, "Create effect");
            go.transform.SetParent(parent, false);
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.playOnAwake = false;
            main.loop = false;
            main.simulationSpace = worldSpace ? ParticleSystemSimulationSpace.World : ParticleSystemSimulationSpace.Local;
            main.scalingMode = ParticleSystemScalingMode.Hierarchy;
            var em = ps.emission;
            em.enabled = true;
            em.rateOverTime = 0f;
            var r = go.GetComponent<ParticleSystemRenderer>();
            r.sharedMaterial = mat;
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.receiveShadows = false;
            return ps;
        }

        static void Fade(ParticleSystem ps, float peakAt, Color tint)
        {
            var col = ps.colorOverLifetime;
            col.enabled = true;
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(tint, 0f), new GradientColorKey(tint, 1f) },
                      new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, peakAt), new GradientAlphaKey(0f, 1f) });
            col.color = g;
        }

        static void Grow(ParticleSystem ps, float from, float to)
        {
            var sol = ps.sizeOverLifetime;
            sol.enabled = true;
            sol.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, from, 1f, to));
        }

        // ------------------------------------------------------------------ effects

        public static ParticleSystem CreateExhaust(Transform point)
        {
            var ps = NewSystem("FX_ExhaustSmoke", point, smokeMat, true);
            var main = ps.main;
            main.loop = true;
            main.playOnAwake = true;
            main.duration = 5f;
            main.startLifetime = new ParticleSystem.MinMaxCurve(1.8f, 2.8f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(1f, 2.2f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.35f, 0.6f);
            main.startColor = new ParticleSystem.MinMaxGradient(new Color(0.2f, 0.2f, 0.2f, 0.5f), new Color(0.34f, 0.33f, 0.32f, 0.42f));
            main.gravityModifier = -0.04f;
            main.maxParticles = 400;
            var em = ps.emission;
            em.rateOverTime = 6f;
            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = 10f;
            shape.radius = 0.06f;
            var noise = ps.noise;
            noise.enabled = true;
            noise.strength = 0.3f;
            noise.frequency = 0.5f;
            Fade(ps, 0.08f, Color.white);
            Grow(ps, 0.6f, 2.6f);
            return ps;
        }

        public static ParticleSystem CreateMuzzleFlash(Transform point)
        {
            var ps = NewSystem("FX_MuzzleFlash", point, additiveMat, false);
            var main = ps.main;
            main.duration = 0.2f;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.05f, 0.09f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0f, 18f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.8f, 1.8f);
            main.startColor = new ParticleSystem.MinMaxGradient(new Color(1f, 0.85f, 0.5f), new Color(1f, 0.55f, 0.2f));
            var em = ps.emission;
            em.SetBursts(new[] { new ParticleSystem.Burst(0f, 8) });
            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = 8f;
            shape.radius = 0.05f;
            Grow(ps, 1f, 0.3f);
            var r = ps.GetComponent<ParticleSystemRenderer>();
            r.renderMode = ParticleSystemRenderMode.Stretch;
            r.velocityScale = 0.03f;
            r.lengthScale = 1.5f;
            return ps;
        }

        public static ParticleSystem CreateMuzzleSmoke(Transform point)
        {
            var ps = NewSystem("FX_MuzzleSmoke", point, smokeMat, true);
            var main = ps.main;
            main.duration = 3f;
            main.startLifetime = new ParticleSystem.MinMaxCurve(1.5f, 3.5f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(1.5f, 9f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.8f, 1.6f);
            main.startColor = new ParticleSystem.MinMaxGradient(new Color(0.55f, 0.53f, 0.5f, 0.55f), new Color(0.42f, 0.4f, 0.38f, 0.5f));
            main.gravityModifier = -0.02f;
            main.maxParticles = 200;
            var em = ps.emission;
            em.SetBursts(new[] { new ParticleSystem.Burst(0f, 30) });
            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = 25f;
            shape.radius = 0.2f;
            var limit = ps.limitVelocityOverLifetime;
            limit.enabled = true;
            limit.limit = 1f;
            limit.dampen = 0.08f;
            var noise = ps.noise;
            noise.enabled = true;
            noise.strength = 0.4f;
            Fade(ps, 0.05f, Color.white);
            Grow(ps, 1f, 3.5f);
            return ps;
        }

        public static Light CreateFlashLight(Transform point)
        {
            var go = new GameObject("FX_MuzzleLight");
            Undo.RegisterCreatedObjectUndo(go, "Create effect");
            go.transform.SetParent(point, false);
            go.transform.localPosition = new Vector3(0f, 0f, 0.5f);
            var l = go.AddComponent<Light>();
            l.type = LightType.Point;
            l.range = 12f;
            l.color = new Color(1f, 0.7f, 0.35f);
            l.intensity = 0f;
            l.shadows = LightShadows.None;
            return l;
        }

        public static ParticleSystem CreateTracer(Transform point)
        {
            var ps = NewSystem("FX_MachineGunTracer", point, additiveMat, true);
            var main = ps.main;
            main.duration = 1f;
            main.startLifetime = 1.2f;
            main.startSpeed = 300f;
            main.startSize = 0.1f;
            main.startColor = new Color(1f, 0.85f, 0.4f);
            main.maxParticles = 200;
            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = 0.6f;
            shape.radius = 0.01f;
            var r = ps.GetComponent<ParticleSystemRenderer>();
            r.renderMode = ParticleSystemRenderMode.Stretch;
            r.velocityScale = 0.02f;
            r.lengthScale = 2f;
            return ps;
        }

        public static ParticleSystem CreateSmallFlash(Transform point)
        {
            var ps = NewSystem("FX_MachineGunFlash", point, additiveMat, false);
            var main = ps.main;
            main.startLifetime = 0.04f;
            main.startSpeed = 0f;
            main.startSize = new ParticleSystem.MinMaxCurve(0.35f, 0.5f);
            main.startColor = new Color(1f, 0.8f, 0.45f);
            var shape = ps.shape;
            shape.enabled = false;
            return ps;
        }

        public static void CreateImpact(Transform root, out ParticleSystem dust, out ParticleSystem sparks)
        {
            dust = NewSystem("FX_ImpactDust", root, smokeMat, true);
            var main = dust.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(1.5f, 3f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(2f, 8f);
            main.startSize = new ParticleSystem.MinMaxCurve(1f, 2.2f);
            main.startColor = new ParticleSystem.MinMaxGradient(new Color(0.45f, 0.4f, 0.33f, 0.7f), new Color(0.32f, 0.3f, 0.27f, 0.65f));
            main.gravityModifier = 0.25f;
            main.maxParticles = 600;
            var shape = dust.shape;
            shape.shapeType = ParticleSystemShapeType.Hemisphere;
            shape.radius = 0.3f;
            shape.rotation = new Vector3(-90f, 0f, 0f);
            var limit = dust.limitVelocityOverLifetime;
            limit.enabled = true;
            limit.limit = 1f;
            limit.dampen = 0.1f;
            Fade(dust, 0.05f, Color.white);
            Grow(dust, 0.6f, 2.5f);

            sparks = NewSystem("FX_ImpactSparks", root, additiveMat, true);
            main = sparks.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.3f, 0.8f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(6f, 18f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.05f, 0.1f);
            main.startColor = new Color(1f, 0.7f, 0.3f);
            main.gravityModifier = 1.5f;
            main.maxParticles = 300;
            shape = sparks.shape;
            shape.shapeType = ParticleSystemShapeType.Hemisphere;
            shape.radius = 0.2f;
            shape.rotation = new Vector3(-90f, 0f, 0f);
            var r = sparks.GetComponent<ParticleSystemRenderer>();
            r.renderMode = ParticleSystemRenderMode.Stretch;
            r.velocityScale = 0.04f;
            r.lengthScale = 1f;
        }

        public static Light CreateHeadlight(Transform point)
        {
            var go = new GameObject("FX_Headlight");
            Undo.RegisterCreatedObjectUndo(go, "Create effect");
            go.transform.SetParent(point, false);
            var l = go.AddComponent<Light>();
            l.type = LightType.Spot;
            l.range = 40f;
            l.spotAngle = 70f;
            l.innerSpotAngle = 35f;
            l.intensity = 5f;
            l.color = new Color(1f, 0.95f, 0.82f);
            l.shadows = LightShadows.None;
            l.enabled = false;
            return l;
        }

        public static Light CreateRearLight(Transform point)
        {
            var go = new GameObject("FX_RearLight");
            Undo.RegisterCreatedObjectUndo(go, "Create effect");
            go.transform.SetParent(point, false);
            go.transform.localPosition = new Vector3(0f, 0f, 0.15f);
            var l = go.AddComponent<Light>();
            l.type = LightType.Point;
            l.range = 3f;
            l.intensity = 1.2f;
            l.color = new Color(1f, 0.1f, 0.05f);
            l.shadows = LightShadows.None;
            l.enabled = false;
            return l;
        }
    }
}
