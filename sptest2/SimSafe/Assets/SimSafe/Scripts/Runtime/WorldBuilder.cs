using SimSafe.Core;
using UnityEngine;

namespace SimSafe
{
    /// <summary>Flashes the red/blue light bar while On.</summary>
    public sealed class LightBar : MonoBehaviour
    {
        public Renderer Red, Blue;
        public bool On = true;
        void Update()
        {
            bool a = Mathf.Repeat(Time.time * 4f, 2f) < 1f;
            Red.enabled = On && a;
            Blue.enabled = On && !a;
        }
    }

    /// <summary>Handles to the scene objects the scenario needs to drive.</summary>
    public sealed class WorldRig
    {
        public Transform Root, PlayerSpawn;
        public float PlayerSpawnYaw = 40f;
        public Humanoid Sam, Dana, Neighbour, Backup;
        public GameObject BackupCar;
        public LightBar CarLights, BackupLights;
        public AudioSource Siren, Mower, Radio, Ambient;
        public Vector3 SamA, SamB, PorchSeat, PorchDoor, RoadPoint, DanaHome, NeighbourA, NeighbourB;

        string samState = "", neighbourState = "";
        Transform playerRef;

        static void Loop(AudioSource a, bool on)
        {
            if (on && !a.isPlaying) a.Play();
            if (!on && a.isPlaying) a.Stop();
        }

        /// <summary>Menu backdrop: calm and quiet.</summary>
        public void SetIdleDisplay()
        {
            CarLights.On = false; BackupLights.On = false;
            Loop(Siren, false); Loop(Mower, false); Loop(Radio, false); Loop(Ambient, true);
            Sam.Teleport(SamA); Sam.Stop(); Sam.SetPose(false, false);
            Dana.Teleport(DanaHome); Neighbour.Teleport(NeighbourA); Neighbour.Stop(); Neighbour.FaceTarget = null;
            BackupCar.SetActive(false); Backup.gameObject.SetActive(false);
            samState = neighbourState = "";
        }

        public void ResetForRun(Transform player)
        {
            playerRef = player;
            samState = neighbourState = "";
            Sam.Teleport(SamA); Sam.Stop(); Sam.SetPose(true, false); Sam.FaceTarget = null;
            Dana.Teleport(DanaHome); Dana.Stop(); Dana.SetPose(false, false); Dana.FaceTarget = player;
            Neighbour.Teleport(NeighbourA); Neighbour.Stop(); Neighbour.FaceTarget = null;
            BackupCar.SetActive(false); Backup.gameObject.SetActive(false);
            Loop(Ambient, true);
        }

        /// <summary>Dana walks over to talk (stage "dana_comes").</summary>
        public void DanaMeet(Vector3 playerPos)
        {
            Vector3 dir = (DanaHome - playerPos); dir.y = 0f;
            Vector3 p = playerPos + dir.normalized * 2.0f; p.y = Mathf.Max(0f, Dana.transform.position.y);
            Dana.MoveTo(new[] { p }, 1.5f, false);
        }

        public void Sync(ScenarioSession s)
        {
            bool sirenOn = !s.HasFlag("sirenOff");
            CarLights.On = sirenOn; Loop(Siren, sirenOn);
            bool mowerOn = !s.HasFlag("neighbourQuiet");
            Loop(Mower, mowerOn);
            Loop(Radio, true);
            Radio.volume = s.HasFlag("radioLow") ? 0.04f : 0.35f;

            if (s.HasFlag("backupArrived") && !BackupCar.activeSelf) { BackupCar.SetActive(true); Backup.gameObject.SetActive(true); }
            BackupLights.On = !s.HasFlag("secondOfficerHandled");

            string nState = mowerOn ? "mow" : "idle";
            if (nState != neighbourState)
            {
                neighbourState = nState;
                if (mowerOn) { Neighbour.FaceTarget = null; Neighbour.MoveTo(new[] { NeighbourA, NeighbourB }, 0.9f, true); }
                else { Neighbour.Stop(); Neighbour.FaceTarget = playerRef; }
            }

            string state;
            if (s.IsFinished) state = "end:" + s.CurrentNode.outcomeTone;
            else if (s.Distress >= 60) state = "pace";
            else if (s.Sensory >= 50) state = "ears";
            else state = "calm";
            if (state != samState) { samState = state; ApplySam(state); }
        }

        void ApplySam(string state)
        {
            switch (state)
            {
                case "pace": Sam.Sit = false; Sam.CoverEars = true; Sam.MoveTo(new[] { SamA, SamB }, 0.9f, true); break;
                case "ears": Sam.Stop(); Sam.SetPose(true, false); break;
                case "calm": Sam.Stop(); Sam.SetPose(false, false); break;
                case "end:positive":
                    Sam.SetPose(false, false);
                    Sam.MoveTo(new[] { new Vector3(PorchSeat.x, 0f, PorchSeat.z - 3f), PorchSeat }, 1.1f, false, () => Sam.Sit = true);
                    Dana.FaceTarget = null; Dana.MoveTo(new[] { PorchSeat + new Vector3(-1.0f, 0f, 0f) }, 1.2f, false);
                    break;
                case "end:mixed":
                    Sam.SetPose(false, false);
                    Sam.MoveTo(new[] { new Vector3(0f, 0f, 4.5f), PorchDoor }, 1.0f, false);
                    Dana.FaceTarget = null; Dana.MoveTo(new[] { new Vector3(-0.8f, 0.2f, 6.5f), PorchDoor + new Vector3(-0.5f, 0f, 0f) }, 1.0f, false);
                    break;
                case "end:difficult":
                    Sam.SetPose(true, false);
                    Sam.MoveTo(new[] { RoadPoint }, 3.2f, false);
                    break;
            }
        }
    }

    /// <summary>Builds the suburban street, house, patrol car, trees, characters, lights and audio from primitives.</summary>
    public static class WorldBuilder
    {
        static readonly Color Grass = new Color(0.33f, 0.50f, 0.26f), Concrete = new Color(0.68f, 0.68f, 0.66f),
            Asphalt = new Color(0.16f, 0.16f, 0.18f), Siding = new Color(0.86f, 0.80f, 0.68f), RoofCol = new Color(0.30f, 0.27f, 0.27f),
            Trim = new Color(0.95f, 0.95f, 0.93f), Glass = new Color(0.16f, 0.22f, 0.28f), Door = new Color(0.18f, 0.38f, 0.45f);

        public static WorldRig Build()
        {
            var rig = new WorldRig();
            var root = new GameObject("World").transform;
            rig.Root = root;

            BuildLighting(root);
            BuildGround(root);
            BuildHouse(root, "House", new Vector3(0f, 0f, 13f), 14f, 10f, true);
            BuildHouse(root, "NeighbourHouse", new Vector3(-25f, 0f, 13f), 12f, 9f, false);
            BuildStreetProps(root);
            BuildTrees(root);

            var carLights = BuildCar(root, "PatrolCar", new Vector3(-4f, 0f, -10f), 90f, true);
            rig.CarLights = carLights;
            var backup = BuildCar(root, "BackupCar", new Vector3(-12.5f, 0f, -10f), 90f, false);
            rig.BackupLights = backup;
            rig.BackupCar = backup.gameObject;

            rig.PlayerSpawn = new GameObject("PlayerSpawn").transform;
            rig.PlayerSpawn.SetParent(root, false);
            rig.PlayerSpawn.position = new Vector3(-3.2f, 0.1f, -7.3f);

            rig.SamA = new Vector3(3.2f, 0f, -3.8f); rig.SamB = new Vector3(6.2f, 0f, -3.8f);
            rig.PorchSeat = new Vector3(1.4f, 0.2f, 6.5f); rig.PorchDoor = new Vector3(0f, 0.2f, 7.6f);
            rig.RoadPoint = new Vector3(4f, 0f, -10.5f);
            rig.DanaHome = new Vector3(-1.4f, 0.2f, 6.8f);
            rig.NeighbourA = new Vector3(-16f, 0f, 1.5f); rig.NeighbourB = new Vector3(-11f, 0f, 1.5f);

            rig.Sam = Humanoid.Create(root, "Sam", rig.SamA, new Color(0.80f, 0.62f, 0.50f), new Color(0.25f, 0.45f, 0.62f), new Color(0.22f, 0.22f, 0.26f), new Color(0.15f, 0.10f, 0.08f));
            MarkCharacter(rig.Sam, "sam", "Sam");
            rig.Dana = Humanoid.Create(root, "Dana", rig.DanaHome, new Color(0.62f, 0.45f, 0.35f), new Color(0.70f, 0.30f, 0.35f), new Color(0.25f, 0.28f, 0.35f), new Color(0.10f, 0.08f, 0.07f));
            MarkCharacter(rig.Dana, "dana", "Dana");
            rig.Neighbour = Humanoid.Create(root, "Neighbour", rig.NeighbourA, new Color(0.85f, 0.68f, 0.55f), new Color(0.80f, 0.75f, 0.35f), new Color(0.35f, 0.32f, 0.28f), new Color(0.45f, 0.40f, 0.35f));
            var nb = rig.Neighbour.gameObject.AddComponent<Interactable>();
            nb.Kind = InteractKind.Object; nb.DisplayName = "Neighbour"; nb.Range = 3.5f;
            nb.InteractionId = s => "neighbour_mower";
            rig.Backup = Humanoid.Create(root, "SecondOfficer", new Vector3(-9.5f, 0f, -8.2f), new Color(0.78f, 0.60f, 0.48f), new Color(0.10f, 0.15f, 0.30f), new Color(0.08f, 0.10f, 0.18f), new Color(0.12f, 0.09f, 0.07f));
            rig.Backup.FaceTarget = null;

            // Mower rides along with the neighbour.
            var mower = new GameObject("Mower").transform;
            mower.SetParent(rig.Neighbour.transform, false);
            mower.localPosition = new Vector3(0f, 0f, 0.9f);
            Gfx.Box(mower, "Deck", new Vector3(0f, 0.25f, 0f), new Vector3(0.6f, 0.3f, 0.8f), Gfx.Lit(new Color(0.75f, 0.15f, 0.12f), 0.3f), false);
            Gfx.Box(mower, "Handle", new Vector3(0f, 0.8f, -0.45f), new Vector3(0.55f, 0.04f, 0.04f), Gfx.Lit(new Color(0.1f, 0.1f, 0.1f)), false);

            // Audio: ambient bed (2D) plus positional sources for siren, mower and radio.
            rig.Ambient = Source(root, "Ambient", AudioSynth.Ambient(), 0.35f, false);
            rig.Siren = Source(carLights.transform, "Siren", AudioSynth.Siren(), 0.5f, true);
            rig.Radio = Source(carLights.transform, "Radio", AudioSynth.Radio(), 0.35f, true, 2f, 25f);
            rig.Mower = Source(mower, "MowerAudio", AudioSynth.Mower(), 0.6f, true);

            // The patrol car and neighbour are interactive objects.
            var carI = carLights.gameObject.AddComponent<Interactable>();
            carI.Kind = InteractKind.Object; carI.DisplayName = "Patrol car"; carI.Range = 3.5f;
            carI.InteractionId = s => !s.HasFlag("sirenOff") ? "car_siren" : (!s.HasFlag("radioLow") ? "car_radio" : null);

            rig.SetIdleDisplay();
            return rig;
        }

        static void MarkCharacter(Humanoid h, string id, string name)
        {
            var i = h.gameObject.AddComponent<Interactable>();
            i.Kind = InteractKind.Character; i.CharacterId = id; i.DisplayName = name; i.Range = 4.5f;
        }

        static AudioSource Source(Transform parent, string name, AudioClip clip, float volume, bool spatial, float min = 5f, float max = 60f)
        {
            var go = new GameObject(name); go.transform.SetParent(parent, false);
            var a = go.AddComponent<AudioSource>();
            a.clip = clip; a.loop = true; a.playOnAwake = false; a.volume = volume;
            a.spatialBlend = spatial ? 1f : 0f;
            if (spatial) { a.rolloffMode = AudioRolloffMode.Linear; a.minDistance = min; a.maxDistance = max; }
            return a;
        }

        // ------------------------------------------------------------------

        static void BuildLighting(Transform root)
        {
            var sunGo = new GameObject("Sun"); sunGo.transform.SetParent(root, false);
            sunGo.transform.rotation = Quaternion.Euler(48f, -35f, 0f);
            var sun = sunGo.AddComponent<Light>();
            sun.type = LightType.Directional; sun.intensity = 1.15f; sun.color = new Color(1f, 0.96f, 0.88f);
            sun.shadows = LightShadows.Soft; sun.shadowStrength = 0.8f;
            RenderSettings.sun = sun;

            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.62f, 0.72f, 0.85f);
            RenderSettings.ambientEquatorColor = new Color(0.55f, 0.58f, 0.60f);
            RenderSettings.ambientGroundColor = new Color(0.30f, 0.32f, 0.28f);
            RenderSettings.fog = true; RenderSettings.fogMode = FogMode.Exponential;
            RenderSettings.fogDensity = 0.004f; RenderSettings.fogColor = new Color(0.74f, 0.82f, 0.90f);

            var sky = Resources.Load<Material>("SimSafe/Sky");
            if (sky == null) { var sh = Shader.Find("Skybox/Procedural"); if (sh != null) sky = new Material(sh); }
            if (sky != null) RenderSettings.skybox = sky;
        }

        static void BuildGround(Transform root)
        {
            var g = new GameObject("Ground").transform; g.SetParent(root, false);
            Gfx.Box(g, "Grass", new Vector3(0f, -0.05f, 10f), new Vector3(160f, 0.1f, 140f), Gfx.Lit(Grass, 0.05f));
            Gfx.Box(g, "Road", new Vector3(0f, 0.01f, -12.2f), new Vector3(160f, 0.04f, 7f), Gfx.Lit(Asphalt, 0.1f));
            for (int x = -70; x < 70; x += 8)
                Gfx.Box(g, "Dash", new Vector3(x, 0.035f, -12.2f), new Vector3(3f, 0.01f, 0.15f), Gfx.Lit(new Color(0.9f, 0.75f, 0.2f)), false);
            Gfx.Box(g, "Curb", new Vector3(0f, 0.06f, -8.6f), new Vector3(160f, 0.12f, 0.3f), Gfx.Lit(new Color(0.75f, 0.75f, 0.73f)));
            Gfx.Box(g, "Sidewalk", new Vector3(0f, 0.03f, -7.5f), new Vector3(160f, 0.06f, 2f), Gfx.Lit(Concrete));
            Gfx.Box(g, "Driveway", new Vector3(4.5f, 0.025f, -0.25f), new Vector3(4f, 0.05f, 16.5f), Gfx.Lit(new Color(0.60f, 0.60f, 0.60f)));
            Gfx.Box(g, "Walkway", new Vector3(0f, 0.025f, -0.5f), new Vector3(1.4f, 0.05f, 12f), Gfx.Lit(Concrete));
        }

        static void BuildHouse(Transform root, string name, Vector3 pos, float width, float depth, bool detailed)
        {
            var h = new GameObject(name).transform; h.SetParent(root, false); h.position = pos;
            float frontZ = -depth * 0.5f;
            Gfx.Box(h, "Walls", new Vector3(0f, 1.5f, 0f), new Vector3(width, 3f, depth), Gfx.Lit(Siding, 0.08f));
            Gfx.Box(h, "Foundation", new Vector3(0f, 0.15f, 0f), new Vector3(width + 0.1f, 0.3f, depth + 0.1f), Gfx.Lit(new Color(0.5f, 0.5f, 0.5f)), false);

            var roof = new GameObject("Roof"); roof.transform.SetParent(h, false);
            roof.transform.localPosition = new Vector3(0f, 3f, 0f);
            roof.AddComponent<MeshFilter>().sharedMesh = Gfx.GableRoof(width + 1.2f, depth + 1.2f, 2.4f);
            roof.AddComponent<MeshRenderer>().sharedMaterial = Gfx.Lit(RoofCol, 0.1f);

            if (!detailed) return;
            float fz = frontZ - 0.03f;
            // Porch
            Gfx.Box(h, "PorchSlab", new Vector3(0f, 0.1f, frontZ - 1.1f), new Vector3(4.4f, 0.2f, 2.2f), Gfx.Lit(Concrete));
            Gfx.Box(h, "PorchStep", new Vector3(0f, 0.05f, frontZ - 2.4f), new Vector3(1.8f, 0.1f, 0.5f), Gfx.Lit(Concrete));
            Gfx.Box(h, "PorchRoof", new Vector3(0f, 2.75f, frontZ - 1.2f), new Vector3(4.8f, 0.12f, 2.6f), Gfx.Lit(RoofCol), false);
            Gfx.Box(h, "PostL", new Vector3(-2.0f, 1.5f, frontZ - 2.0f), new Vector3(0.14f, 2.6f, 0.14f), Gfx.Lit(Trim));
            Gfx.Box(h, "PostR", new Vector3(2.0f, 1.5f, frontZ - 2.0f), new Vector3(0.14f, 2.6f, 0.14f), Gfx.Lit(Trim));
            // Front door, garage door, windows
            Gfx.Box(h, "DoorFrame", new Vector3(0f, 1.3f, fz), new Vector3(1.3f, 2.2f, 0.05f), Gfx.Lit(Trim), false);
            Gfx.Box(h, "FrontDoor", new Vector3(0f, 1.25f, fz - 0.03f), new Vector3(1.0f, 2.1f, 0.06f), Gfx.Lit(Door, 0.3f), false);
            Gfx.Box(h, "GarageDoor", new Vector3(4.5f, 1.3f, fz - 0.02f), new Vector3(4f, 2.4f, 0.06f), Gfx.Lit(new Color(0.88f, 0.88f, 0.86f)), false);
            for (int i = 1; i < 4; i++)
                Gfx.Box(h, "GarageLine", new Vector3(4.5f, 0.1f + i * 0.6f, fz - 0.055f), new Vector3(3.9f, 0.02f, 0.02f), Gfx.Lit(new Color(0.6f, 0.6f, 0.6f)), false);
            foreach (float wx in new[] { -4.9f, -3.1f })
            {
                Gfx.Box(h, "WindowFrame", new Vector3(wx, 1.7f, fz), new Vector3(1.5f, 1.35f, 0.05f), Gfx.Lit(Trim), false);
                Gfx.Box(h, "Window", new Vector3(wx, 1.7f, fz - 0.03f), new Vector3(1.3f, 1.15f, 0.05f), Gfx.Lit(Glass, 0.8f), false);
            }
            Gfx.Box(h, "Chimney", new Vector3(-4.5f, 4.6f, 2f), new Vector3(0.7f, 2.2f, 0.7f), Gfx.Lit(new Color(0.55f, 0.35f, 0.30f)), false);
        }

        static void BuildStreetProps(Transform root)
        {
            var p = new GameObject("Props").transform; p.SetParent(root, false);
            Gfx.Box(p, "Hedge", new Vector3(-8.5f, 0.45f, 8f), new Vector3(0.8f, 0.9f, 22f), Gfx.Lit(new Color(0.16f, 0.35f, 0.17f), 0.05f));
            // Mailbox
            Gfx.Box(p, "MailPost", new Vector3(7.6f, 0.55f, -6.0f), new Vector3(0.1f, 1.1f, 0.1f), Gfx.Lit(new Color(0.3f, 0.22f, 0.15f)));
            Gfx.Box(p, "Mailbox", new Vector3(7.6f, 1.2f, -6.0f), new Vector3(0.28f, 0.25f, 0.5f), Gfx.Lit(new Color(0.15f, 0.2f, 0.45f), 0.3f));
            // Lamp post
            Gfx.Cyl(p, "LampPole", new Vector3(10f, 2.2f, -7.1f), 0.07f, 4.4f, Gfx.Lit(new Color(0.15f, 0.15f, 0.17f)), true);
            Gfx.Ball(p, "LampHead", new Vector3(10f, 4.5f, -7.1f), 0.4f, Gfx.Unlit(new Color(1f, 0.95f, 0.8f)));
            // Garden beds by the porch
            Gfx.Box(p, "BedL", new Vector3(-3.0f, 0.15f, 7.0f), new Vector3(1.4f, 0.3f, 1.0f), Gfx.Lit(new Color(0.28f, 0.2f, 0.14f)), false);
            Gfx.Box(p, "ShrubL", new Vector3(-3.0f, 0.55f, 7.0f), new Vector3(1.1f, 0.7f, 0.8f), Gfx.Lit(new Color(0.2f, 0.42f, 0.2f)), false);
        }

        static void BuildTrees(Transform root)
        {
            var t = new GameObject("Trees").transform; t.SetParent(root, false);
            var rng = new System.Random(7);
            Vector3[] near = { new Vector3(-10.5f, 0f, 3f), new Vector3(10f, 0f, 5f), new Vector3(-9.8f, 0f, 14f), new Vector3(11.5f, 0f, 14f), new Vector3(-20f, 0f, 3f) };
            foreach (var p in near) Tree(t, p, 1.0f + (float)rng.NextDouble() * 0.3f);
            for (int x = -64; x <= 64; x += 9)
                Tree(t, new Vector3(x + (float)rng.NextDouble() * 4f, 0f, 30f + (float)rng.NextDouble() * 6f), 1.3f + (float)rng.NextDouble() * 0.5f);
            for (int x = -60; x <= 60; x += 14)
                Tree(t, new Vector3(x + (float)rng.NextDouble() * 4f, 0f, -26f - (float)rng.NextDouble() * 4f), 1.2f + (float)rng.NextDouble() * 0.4f);
        }

        static void Tree(Transform parent, Vector3 pos, float scale)
        {
            var tr = new GameObject("Tree").transform; tr.SetParent(parent, false); tr.position = pos;
            var bark = Gfx.Lit(new Color(0.30f, 0.21f, 0.14f), 0.05f);
            Gfx.Cyl(tr, "Trunk", new Vector3(0f, 1.5f * scale, 0f), 0.2f * scale, 3f * scale, bark, true);
            var leaf = Gfx.Lit(new Color(0.17f, 0.38f + 0.06f * (scale - 1f), 0.18f), 0.05f);
            Gfx.Ball(tr, "Foliage1", new Vector3(0f, 4.0f * scale, 0f), 3.6f * scale, leaf);
            Gfx.Ball(tr, "Foliage2", new Vector3(0.9f * scale, 3.3f * scale, 0.4f * scale), 2.6f * scale, leaf);
            Gfx.Ball(tr, "Foliage3", new Vector3(-0.8f * scale, 3.5f * scale, -0.5f * scale), 2.4f * scale, leaf);
        }

        static LightBar BuildCar(Transform root, string name, Vector3 pos, float yaw, bool withText)
        {
            var car = new GameObject(name).transform; car.SetParent(root, false);
            car.position = pos; car.rotation = Quaternion.Euler(0f, yaw, 0f);
            var black = Gfx.Lit(new Color(0.06f, 0.07f, 0.09f), 0.5f);
            var white = Gfx.Lit(new Color(0.93f, 0.93f, 0.95f), 0.5f);
            var glass = Gfx.Lit(new Color(0.12f, 0.17f, 0.22f), 0.9f);
            Gfx.Box(car, "Body", new Vector3(0f, 0.62f, 0f), new Vector3(1.9f, 0.62f, 4.6f), black);
            Gfx.Box(car, "Doors", new Vector3(0f, 0.66f, -0.1f), new Vector3(1.93f, 0.44f, 1.9f), white, false);
            Gfx.Box(car, "Cabin", new Vector3(0f, 1.2f, -0.3f), new Vector3(1.66f, 0.5f, 2.2f), glass);
            Gfx.Box(car, "Roof", new Vector3(0f, 1.47f, -0.3f), new Vector3(1.62f, 0.06f, 1.9f), black, false);
            Gfx.Box(car, "BarBase", new Vector3(0f, 1.54f, -0.3f), new Vector3(1.1f, 0.07f, 0.28f), black, false);
            var red = Gfx.Box(car, "BarRed", new Vector3(-0.28f, 1.61f, -0.3f), new Vector3(0.5f, 0.1f, 0.26f), Gfx.Unlit(new Color(1f, 0.1f, 0.1f)), false);
            var blue = Gfx.Box(car, "BarBlue", new Vector3(0.28f, 1.61f, -0.3f), new Vector3(0.5f, 0.1f, 0.26f), Gfx.Unlit(new Color(0.15f, 0.3f, 1f)), false);
            var tyre = Gfx.Lit(new Color(0.05f, 0.05f, 0.05f), 0.1f);
            foreach (float sx in new[] { -0.92f, 0.92f })
                foreach (float sz in new[] { -1.4f, 1.4f })
                    Gfx.Cyl(car, "Wheel", new Vector3(sx, 0.34f, sz), 0.34f, 0.24f, tyre, false, new Vector3(0f, 0f, 90f));
            foreach (float sx in new[] { -0.6f, 0.6f })
            {
                Gfx.Box(car, "Headlight", new Vector3(sx, 0.7f, 2.31f), new Vector3(0.35f, 0.14f, 0.04f), Gfx.Unlit(new Color(1f, 0.97f, 0.85f)), false);
                Gfx.Box(car, "Taillight", new Vector3(sx, 0.7f, -2.31f), new Vector3(0.35f, 0.14f, 0.04f), Gfx.Unlit(new Color(0.8f, 0.05f, 0.05f)), false);
            }
            if (withText)
            {
                foreach (float side in new[] { -1f, 1f })
                {
                    var tgo = new GameObject("Text"); tgo.transform.SetParent(car, false);
                    tgo.transform.localPosition = new Vector3(0.97f * side, 0.66f, -0.1f);
                    tgo.transform.localRotation = Quaternion.Euler(0f, side > 0 ? -90f : 90f, 0f);
                    var tm = tgo.AddComponent<TextMesh>();
                    tm.text = "POLICE"; tm.font = Gfx.BuiltinFont; tm.fontSize = 64; tm.characterSize = 0.045f;
                    tm.anchor = TextAnchor.MiddleCenter; tm.color = new Color(0.08f, 0.14f, 0.32f);
                    tgo.GetComponent<MeshRenderer>().sharedMaterial = tm.font.material;
                }
            }
            var bar = car.gameObject.AddComponent<LightBar>();
            bar.Red = red.GetComponent<Renderer>(); bar.Blue = blue.GetComponent<Renderer>();
            return bar;
        }
    }
}
