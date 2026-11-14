using UnityEditor;
using UnityEngine;

namespace TankRig.Editor
{
    /// <summary>
    /// Tools > Tank Rig > 戦車組み立てウィンドウ: pick hull / turret / gun, place the tracks (mirrored) and the effect points
    /// with the mouse in the Scene view, then rig the tank so it can be driven with WASD + mouse.
    /// </summary>
    public class TankRigBuilderWindow : EditorWindow
    {
        [MenuItem("Tools/Tank Rig/戦車組み立てウィンドウ", priority = 1)]
        public static void Open()
        {
            var w = GetWindow<TankRigBuilderWindow>("戦車組み立て");
            w.minSize = new Vector2(380f, 520f);
            w.Show();
        }

        enum FocusMode { None, Track, Point }

        GameObject model;
        Transform hull, turret, gun, barrel;
        bool flipForward;
        TankRigSetup setup;
        Vector2 scroll;
        bool createGround = true, setupCamera = true, linkDetails, showArms = true;
        float suspensionPreview;
        TankRigBuilder.PointKind? placing;
        FocusMode focus = FocusMode.Track;
        TankRigBuilder.PointKind focusPoint;
        string status;
        GUIStyle sectionStyle, bigButton;

        void OnEnable()
        {
            SceneView.duringSceneGui += OnSceneGUI;
            Selection.selectionChanged += OnSelectionChanged;
            OnSelectionChanged();
        }

        void OnDisable()
        {
            SceneView.duringSceneGui -= OnSceneGUI;
            Selection.selectionChanged -= OnSelectionChanged;
            placing = null;
            ResetPreview();
        }

        void OnSelectionChanged()
        {
            var go = Selection.activeGameObject;
            if (go)
            {
                var s = go.GetComponentInParent<TankRigSetup>();
                if (s) setup = s;
            }
            Repaint();
        }

        // ------------------------------------------------------------------ window

        void Styles()
        {
            if (sectionStyle != null) return;
            sectionStyle = new GUIStyle(EditorStyles.boldLabel) { fontSize = 13 };
            bigButton = new GUIStyle(GUI.skin.button) { fontSize = 13, fontStyle = FontStyle.Bold };
        }

        void Section(string title)
        {
            EditorGUILayout.Space(10f);
            EditorGUILayout.LabelField(title, sectionStyle);
            var r = EditorGUILayout.GetControlRect(false, 1f);
            EditorGUI.DrawRect(r, new Color(0.5f, 0.5f, 0.5f, 0.5f));
        }

        void OnGUI()
        {
            Styles();
            scroll = EditorGUILayout.BeginScrollView(scroll);
            EditorGUILayout.HelpBox("SMC-AD などで取り込んだ戦車に履帯を組み合わせ、WASD とマウスで操縦できる戦車にします。上から順に進めてください。", MessageType.None);

            bool playing = EditorApplication.isPlayingOrWillChangePlaymode;
            if (playing)
                EditorGUILayout.HelpBox("プレイ中は組み立てできません（変更はプレイ終了で消えるため）。■ で止めてから操作してください。", MessageType.Warning);
            using (new EditorGUI.DisabledScope(playing))
            {
                if (!setup) DrawStart();
                else
                {
                    DrawHeader();
                    DrawTracks();
                    DrawSuspension();
                    DrawPoints();
                    DrawSettings();
                    DrawFinish();
                }
            }
            if (!string.IsNullOrEmpty(status)) EditorGUILayout.HelpBox(status, MessageType.Info);
            EditorGUILayout.Space(12f);
            EditorGUILayout.EndScrollView();
        }

        void DrawStart()
        {
            Section("① 戦車モデルと部位");
            EditorGUI.BeginChangeCheck();
            model = (GameObject)EditorGUILayout.ObjectField("戦車モデル", model, typeof(GameObject), true);
            if (EditorGUI.EndChangeCheck() && model && !EditorUtility.IsPersistent(model))
                TankRigBuilder.AutoDetect(model, out hull, out turret, out gun, out barrel);

            if (model && EditorUtility.IsPersistent(model))
            {
                EditorGUILayout.HelpBox("シーンに置いた戦車を指定してください。", MessageType.Warning);
                if (GUILayout.Button("このモデルをシーンに置く"))
                {
                    var inst = (GameObject)PrefabUtility.InstantiatePrefab(model);
                    Undo.RegisterCreatedObjectUndo(inst, "戦車をシーンに置く");
                    model = inst;
                    Selection.activeGameObject = inst;
                    TankRigBuilder.AutoDetect(model, out hull, out turret, out gun, out barrel);
                }
                return;
            }
            if (!model && Selection.activeGameObject && !EditorUtility.IsPersistent(Selection.activeGameObject)
                && GUILayout.Button("選択中のオブジェクトを使う"))
            {
                model = Selection.activeGameObject.transform.root.gameObject;
                TankRigBuilder.AutoDetect(model, out hull, out turret, out gun, out barrel);
            }

            using (new EditorGUI.DisabledScope(!model))
            {
                if (GUILayout.Button("部位を自動検出（SMC-AD のジョイントから）"))
                    TankRigBuilder.AutoDetect(model, out hull, out turret, out gun, out barrel);
            }
            hull = (Transform)EditorGUILayout.ObjectField("車体", hull, typeof(Transform), true);
            turret = (Transform)EditorGUILayout.ObjectField("砲塔", turret, typeof(Transform), true);
            gun = (Transform)EditorGUILayout.ObjectField(new GUIContent("砲座（上下）", "必須。主砲の上下（俯仰）で回る部分"), gun, typeof(Transform), true);
            barrel = (Transform)EditorGUILayout.ObjectField(new GUIContent("砲身（後退）", "なくても可。発砲時に前後に後退する部分。指定しないと後退は起きません"), barrel, typeof(Transform), true);
            if (!gun) EditorGUILayout.HelpBox("砲座は必須です。主砲の上下に回る部分を指定してください。", MessageType.Warning);
            string barrelProblem = TankRigBuilder.BarrelProblem(gun, barrel);
            if (barrelProblem != null) EditorGUILayout.HelpBox(barrelProblem, MessageType.Error);
            else if (gun && !barrel) EditorGUILayout.HelpBox("砲身なし: 発砲しても砲身は後退しません（車体の反動・発砲炎はあります）。", MessageType.None);
            flipForward = EditorGUILayout.ToggleLeft("前後を反転する（前方向は主砲の向きから自動で決まります）", flipForward);

            EditorGUILayout.Space(6f);
            using (new EditorGUI.DisabledScope(!model || !hull || !gun || barrelProblem != null))
            {
                if (GUILayout.Button("組み立てを始める", bigButton, GUILayout.Height(34f)))
                {
                    setup = TankRigBuilder.CreateSetup(model, hull, turret, gun, barrel, flipForward);
                    AutoAssignTrackAssets();
                    Selection.activeGameObject = setup.gameObject;
                    status = "組み立て用のルート「" + setup.name + "」を作り、各ポイントを仮配置しました。次は履帯です。";
                    SceneView.lastActiveSceneView?.FrameSelected();
                }
            }
        }

        void DrawHeader()
        {
            Section("戦車");
            using (new EditorGUI.DisabledScope(true))
                EditorGUILayout.ObjectField("組み立て中", setup, typeof(TankRigSetup), true);
            EditorGUILayout.LabelField("回転軸", setup.axesFromJoints ? "SMC-AD のジョイントから取得" : "形から推定");
            using (new EditorGUI.DisabledScope(true))
                EditorGUILayout.ObjectField(new GUIContent("砲座（上下）"), setup.gun, typeof(Transform), true);
            EditorGUI.BeginChangeCheck();
            var b = (Transform)EditorGUILayout.ObjectField(new GUIContent("砲身（後退）", "なくても可。指定しないと発砲時の後退は起きません"), setup.barrel, typeof(Transform), true);
            if (EditorGUI.EndChangeCheck())
            {
                string problem = TankRigBuilder.SetBarrel(setup, b);
                status = problem ?? (b ? "砲身を指定しました。砲口は砲身と一緒に動きます。反映するには「戦車を完成させる」を押してください。"
                                       : "砲身を外しました（発砲時の後退なし）。反映するには「戦車を完成させる」を押してください。");
            }
            if (!setup.gun) EditorGUILayout.HelpBox("砲座がありません。「別の戦車を組み立てる」から砲座を指定し直してください。", MessageType.Warning);
            else if (!setup.barrel) EditorGUILayout.HelpBox("砲身なし: 発砲しても砲身は後退しません。", MessageType.None);
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("前後を反転"))
                {
                    TankRigBuilder.FlipForward(setup);
                    status = "前後を反転し、ポイントを置き直しました。";
                }
                if (GUILayout.Button("別の戦車を組み立てる"))
                {
                    setup = null;
                    model = null;
                    hull = turret = gun = barrel = null;
                    status = null;
                    GUIUtility.ExitGUI();
                }
            }
        }

        void AutoAssignTrackAssets()
        {
            if (!setup) return;
            TankRigAssets.EnsureDefaultTracks();
            Undo.RecordObject(setup, "履帯の指定");
            if (!setup.trackAsset) setup.trackAsset = TankRigAssets.LoadTrack("TankTrack_6W");
            if (!setup.linkAsset) setup.linkAsset = TankRigAssets.LoadTrack("TankTrack_Link");
        }

        /// <summary>One button per track link design (all share the same pitch / width, so they can be swapped at any time).</summary>
        void DrawLinkDesigns()
        {
            var designs = TankRigAssets.LinkDesigns();
            if (designs.Count == 0) return;
            EditorGUILayout.LabelField("履帯のデザイン");
            int current = designs.FindIndex(d => d.asset == setup.linkAsset);
            const int perRow = 3;
            for (int row = 0; row < designs.Count; row += perRow)
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    for (int i = row; i < Mathf.Min(row + perRow, designs.Count); i++)
                    {
                        bool on = i == current;
                        bool now = GUILayout.Toggle(on, designs[i].label, EditorStyles.miniButton, GUILayout.Height(22f));
                        if (now && !on)
                        {
                            TankRigBuilder.SetLinkDesign(setup, designs[i].asset);
                            status = "履帯のデザインを「" + designs[i].label + "」にしました。";
                            SceneView.RepaintAll();
                        }
                    }
                }
            }
            if (designs.Count < TankRigAssets.BuiltInLinks.Length)
                EditorGUILayout.HelpBox("取り込まれていないデザインがあります。「同梱の履帯を取り込む」を押すと全部使えるようになります。", MessageType.None);
        }

        void ImportTracksFrom(string src)
        {
            int n = TankRigAssets.ImportTracks(src);
            AutoAssignTrackAssets();
            status = n > 0 ? $"履帯の FBX を {n} 個取り込みました（{TankRigAssets.TrackFolder}）。" : "FBX が見つかりませんでした: " + src;
        }

        void DrawTracks()
        {
            Section("② 履帯");
            EditorGUI.BeginChangeCheck();
            var ta = (GameObject)EditorGUILayout.ObjectField("転輪一式（FBX）", setup.trackAsset, typeof(GameObject), false);
            var la = (GameObject)EditorGUILayout.ObjectField("履帯リンク（FBX）", setup.linkAsset, typeof(GameObject), false);
            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(setup, "履帯の指定");
                setup.trackAsset = ta;
                if (la != setup.linkAsset)
                {
                    if (la && setup.trackLeft) TankRigBuilder.SetLinkDesign(setup, la);
                    else setup.linkAsset = la;
                }
            }
            DrawLinkDesigns();

            using (new EditorGUILayout.HorizontalScope())
            {
                foreach (var n in new[] { "6W", "7W" })
                {
                    var asset = TankRigAssets.LoadTrack("TankTrack_" + n);
                    using (new EditorGUI.DisabledScope(!asset))
                        if (GUILayout.Button(n == "6W" ? "6輪" : "7輪"))
                        {
                            Undo.RecordObject(setup, "履帯の指定");
                            setup.trackAsset = asset;
                            if (!setup.linkAsset) setup.linkAsset = TankRigAssets.LoadTrack("TankTrack_Link");
                            if (setup.trackLeft) TankRigBuilder.PlaceTracks(setup, false);
                        }
                }
                if (GUILayout.Button(new GUIContent("同梱の履帯を取り込む", "パッケージに入っている転輪・履帯の FBX を " + TankRigAssets.TrackFolder + " にコピーし直します")))
                    ImportTracksFrom(TankRigAssets.DefaultSourceFolder());
                if (GUILayout.Button(new GUIContent("フォルダから…", "自分で作った履帯の FBX（TankTrack_*.fbx）があるフォルダを選んで取り込みます"), GUILayout.Width(84f)))
                {
                    string src = EditorUtility.OpenFolderPanel("履帯の FBX があるフォルダ", "", "");
                    if (!string.IsNullOrEmpty(src)) ImportTracksFrom(src);
                }
            }

            using (new EditorGUI.DisabledScope(!setup.trackAsset || !setup.linkAsset))
            {
                if (GUILayout.Button(setup.trackLeft ? "履帯を置き直す（自動フィット）" : "履帯を配置する（自動フィット）", GUILayout.Height(26f)))
                {
                    TankRigBuilder.PlaceTracks(setup, true);
                    focus = FocusMode.Track;
                    status = "左右の履帯を置きました。シーンビューで左の履帯を動かすと、右も左右対称に動きます。";
                    SceneView.RepaintAll();
                }
            }
            if (!setup.trackLeft) return;

            EditorGUI.BeginChangeCheck();
            var o = setup.trackOffset;
            float x = EditorGUILayout.Slider("左右の間隔（片側）", o.x, 0f, 6f);
            float y = EditorGUILayout.Slider("高さ", o.y, -5f, 5f);
            float z = EditorGUILayout.Slider("前後", o.z, -8f, 8f);
            float sc = EditorGUILayout.Slider("大きさ", setup.trackScale, 0.2f, 4f);
            float th = EditorGUILayout.Slider("厚さ（倍率）", setup.trackThickness, 0.3f, 3f);
            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(setup, "履帯の調整");
                Undo.RecordObject(setup.trackLeft, "履帯の調整");
                if (setup.trackRight) Undo.RecordObject(setup.trackRight, "履帯の調整");
                bool scaleChanged = !Mathf.Approximately(sc, setup.trackScale);
                setup.trackOffset = new Vector3(x, y, z);
                setup.trackScale = sc;
                setup.trackThickness = th;
                setup.ApplyTrackPose();
                if (scaleChanged) TankRigBuilder.ApplyTrackSettings(setup, suspensionPreview); // metres → new local units
                SceneView.RepaintAll();
            }
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button(focus == FocusMode.Track ? "● シーンで履帯を動かす" : "シーンで履帯を動かす")) { focus = FocusMode.Track; SceneView.RepaintAll(); }
                if (GUILayout.Button("自動フィットし直す"))
                {
                    TankRigBuilder.RecordTrackTransforms(setup, "履帯の自動フィット");
                    TankRigBuilder.AutoFitTracks(setup);
                    setup.ApplyTrackPose();
                    TankRigBuilder.ApplyTrackSettings(setup, suspensionPreview); // metres → the new scale's local units
                }
            }
            var tt = setup.trackLeft.GetComponent<TankTrack>();
            if (tt) EditorGUILayout.LabelField("リンク", $"片側 {tt.LinkCount} 枚 / 一周 {tt.PathLength * setup.trackScale:0.00} m");

            linkDetails = EditorGUILayout.Foldout(linkDetails, "リンクの詳細");
            if (linkDetails)
            {
                EditorGUI.BeginChangeCheck();
                float pitch = EditorGUILayout.FloatField("ピッチ（リンクの間隔）", setup.linkPitch);
                float off = EditorGUILayout.FloatField("転輪表面からピンまで", setup.linkPitchLineOffset);
                float depth = EditorGUILayout.FloatField("ピンから接地面まで", setup.linkGroundDepth);
                if (EditorGUI.EndChangeCheck())
                {
                    Undo.RecordObject(setup, "リンクの設定");
                    setup.linkPitch = Mathf.Max(0.02f, pitch);
                    setup.linkPitchLineOffset = off;
                    setup.linkGroundDepth = Mathf.Max(0f, depth);
                    TankRigBuilder.RefreshTracks(setup);
                }
            }
        }

        void DrawSuspension()
        {
            Section("②' サスペンション（トーションバー）");
            EditorGUILayout.HelpBox("いまの転輪の位置が「地面に置いたときの静止位置」です。荷重がないと少し下がりながら後ろへ弧を描き、押されると上がります。履帯は伸び切ったときにピンと張る長さなので、静止時は上側が少したわみます。", MessageType.None);
            EditorGUI.BeginChangeCheck();
            bool on = EditorGUILayout.ToggleLeft("サスペンションを使う", setup.suspension);
            float droop, bump, arm, angle, sag, sway;
            using (new EditorGUI.DisabledScope(!on))
            {
                droop = EditorGUILayout.Slider("伸び（荷重なし, m）", setup.suspensionDroop, 0f, 0.4f);
                bump = EditorGUILayout.Slider("縮み（m）", setup.suspensionBump, 0f, 0.5f);
                arm = EditorGUILayout.Slider("アームの長さ（m）", setup.armLength, 0.1f, 1.5f);
                angle = EditorGUILayout.Slider("アームの角度（度）", setup.armAngle, 0f, 45f);
                sag = EditorGUILayout.Slider("履帯のたわみの上限（m）", setup.maxSag, 0f, 0.4f);
                sway = EditorGUILayout.Slider("車体の揺れ", setup.bodySway, 0f, 3f);
            }
            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(setup, "サスペンションの設定");
                setup.suspension = on;
                setup.suspensionDroop = droop;
                setup.suspensionBump = bump;
                setup.armLength = arm;
                setup.armAngle = angle;
                setup.maxSag = sag;
                setup.bodySway = sway;
                TankRigBuilder.ApplyTrackSettings(setup, suspensionPreview);
                SceneView.RepaintAll();
            }
            if (!setup.trackLeft || !setup.suspension) return;

            EditorGUI.BeginChangeCheck();
            float p = EditorGUILayout.Slider("プレビュー（縮み ← 静止 → 伸び）", suspensionPreview, -1f, 1f);
            if (EditorGUI.EndChangeCheck())
            {
                suspensionPreview = Mathf.Abs(p) < 0.04f ? 0f : p;
                TankRigBuilder.ApplyTrackSettings(setup, suspensionPreview);
                SceneView.RepaintAll();
            }
            var tt = setup.trackLeft.GetComponent<TankTrack>();
            if (tt)
                EditorGUILayout.LabelField("履帯のたわみ", tt.CurrentSag > 1e-4f
                    ? $"{tt.CurrentSag * setup.trackScale * 100f:0.0} cm"
                    : "なし（ピンと張っています）");
            showArms = EditorGUILayout.ToggleLeft("シーンビューにアームの動き（弧）を表示", showArms);
        }

        void ResetPreview()
        {
            if (suspensionPreview == 0f || !setup) return;
            suspensionPreview = 0f;
            TankRigBuilder.ApplyTrackSettings(setup, 0f);
        }

        void DrawPoints()
        {
            Section("③ ポイント（シーンビューでクリックして配置）");
            EditorGUILayout.HelpBox("「クリックで配置」を押してからシーンビューで戦車の表面をクリックします。置いた後は矢印で動かせます。向きは回転ツール（E キー）で変えられます。", MessageType.None);
            foreach (var k in TankRigBuilder.AllPoints) PointRow(k);

            EditorGUI.BeginChangeCheck();
            bool coax = EditorGUILayout.ToggleLeft("機銃を砲座と一緒に上下させる（同軸機銃）", setup.machineGunFollowsGun);
            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(setup, "同軸機銃");
                setup.machineGunFollowsGun = coax;
                if (setup.machineGun) Undo.SetTransformParent(setup.machineGun, TankRigBuilder.PointParent(setup, TankRigBuilder.PointKind.MachineGun), "同軸機銃");
            }
            if (GUILayout.Button("すべて自動で仮配置し直す"))
            {
                TankRigBuilder.AutoPlacePoints(setup, true);
                SceneView.RepaintAll();
            }
        }

        void PointRow(TankRigBuilder.PointKind k)
        {
            var t = TankRigBuilder.GetPoint(setup, k);
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField((t ? "✔ " : "－ ") + TankRigBuilder.Label(k), GUILayout.Width(120f));
                bool active = placing == k;
                var bg = GUI.backgroundColor;
                if (active) GUI.backgroundColor = new Color(0.4f, 1f, 0.5f);
                if (GUILayout.Button(active ? "クリック待ち…" : "クリックで配置"))
                {
                    placing = active ? (TankRigBuilder.PointKind?)null : k;
                    SceneView.lastActiveSceneView?.Focus();
                }
                GUI.backgroundColor = bg;
                using (new EditorGUI.DisabledScope(!t))
                    if (GUILayout.Button("選ぶ", GUILayout.Width(44f)))
                    {
                        focus = FocusMode.Point;
                        focusPoint = k;
                        SceneView.RepaintAll();
                    }
                if (TankRigBuilder.CanMirror(k))
                {
                    bool m = TankRigBuilder.IsMirrored(setup, k);
                    bool nm = GUILayout.Toggle(m, "左右対称", GUILayout.Width(72f));
                    if (nm != m) TankRigBuilder.SetMirrored(setup, k, nm);
                }
                else GUILayout.Space(76f);
            }
        }

        void DrawSettings()
        {
            Section("④ 走行・武装");
            EditorGUI.BeginChangeCheck();
            float maxSpeed = EditorGUILayout.Slider("最高速度（km/h）", setup.maxSpeedKmh, 5f, 120f);
            float rev = EditorGUILayout.Slider("後退速度（km/h）", setup.reverseSpeedKmh, 2f, 60f);
            float acc = EditorGUILayout.Slider("加速（m/s²）", setup.acceleration, 0.5f, 15f);
            float brk = EditorGUILayout.Slider("ブレーキ（m/s²、キーを離すと止まる）", setup.braking, 1f, 20f);
            float resp = EditorGUILayout.Slider("操作の反応（大きいほどキビキビ）", setup.responsiveness, 0.3f, 3f);
            float turn = EditorGUILayout.Slider("車体の旋回（度/秒）", setup.turnRate, 5f, 120f);
            float trav = EditorGUILayout.Slider("砲塔の旋回（度/秒）", setup.traverseSpeed, 2f, 120f);
            float minE = setup.minElevation, maxE = setup.maxElevation;
            EditorGUILayout.MinMaxSlider($"俯仰角 {minE:0}°〜{maxE:0}°", ref minE, ref maxE, -30f, 70f);
            float reload = EditorGUILayout.Slider("装填時間（秒）", setup.reloadTime, 0.2f, 15f);
            float power = EditorGUILayout.Slider(new GUIContent("主砲火力", "反動・のけぞり、砲身の後退量、カメラの揺れ、発砲炎の大きさがこれに比例します（1 = 標準）"), setup.gunPower, 0.1f, 4f);
            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(setup, "走行・武装の設定");
                setup.maxSpeedKmh = maxSpeed;
                setup.reverseSpeedKmh = rev;
                setup.acceleration = acc;
                setup.braking = brk;
                setup.responsiveness = resp;
                setup.turnRate = turn;
                setup.traverseSpeed = trav;
                setup.minElevation = Mathf.Round(minE);
                setup.maxElevation = Mathf.Round(maxE);
                setup.reloadTime = reload;
                setup.gunPower = power;
            }

            EditorGUILayout.Space(4f);
            EditorGUI.BeginChangeCheck();
            bool phys = EditorGUILayout.ToggleLeft("物理演算で走る（車体だけ。履帯は描画で地形に沿わせる）", setup.physicsDrive);
            float tons = setup.massTons;
            using (new EditorGUI.DisabledScope(!phys))
                tons = EditorGUILayout.Slider("車重（トン）", setup.massTons, 1f, 120f);
            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(setup, "物理演算の設定");
                setup.physicsDrive = phys;
                setup.massTons = tons;
            }
            EditorGUILayout.HelpBox(setup.physicsDrive
                ? "転輪ごとのトーションバーが車体を支え、左右の履帯の駆動力で走ります。坂・段差・障害物・反動が車体の動きに出ます。"
                : "物理演算なし：地面に沿って滑らかに動きます（軽い。AI の大量配置向け）。", MessageType.None);
            if (setup.physicsDrive && (!setup.trackLeft || !setup.suspension))
                EditorGUILayout.HelpBox("物理演算の走行は転輪のサスペンションで車体を押すので、履帯がない・サスペンションを使わないときは自動で「物理演算なし」になります。", MessageType.Warning);
        }

        void DrawFinish()
        {
            Section("⑤ 仕上げ");
            createGround = EditorGUILayout.ToggleLeft("テスト用の地面を作る（坂・丘・的・壁・石・丸太・段差つき）", createGround);
            setupCamera = EditorGUILayout.ToggleLeft("メインカメラを戦車の三人称カメラにする", setupCamera);
            if (!setup.trackLeft) EditorGUILayout.HelpBox("履帯がまだありません（履帯なしでも完成させられます）。", MessageType.Warning);
            if (!setup.gun) EditorGUILayout.HelpBox("砲座が指定されていないので完成させられません。", MessageType.Error);
            using (new EditorGUI.DisabledScope(!setup.gun))
            if (GUILayout.Button("戦車を完成させる（リギング）", bigButton, GUILayout.Height(36f)))
            {
                suspensionPreview = 0f;
                var c = TankRigBuilder.Complete(setup, createGround, setupCamera);
                Selection.activeGameObject = c.gameObject;
                status = "完成しました。▶ で再生すると、WASD で走行、マウスで照準、左クリックで主砲、Space で機銃、右クリックでズーム、L でライトです。設定を変えたら、もう一度このボタンを押してください。";
            }
            using (new EditorGUI.DisabledScope(!setup.GetComponent<TankController>() || EditorApplication.isPlayingOrWillChangePlaymode))
            {
                if (GUILayout.Button("▶ プレイして試す", GUILayout.Height(28f)))
                {
                    ResetPreview();
                    EditorApplication.isPlaying = true;
                }
            }
        }

        // ------------------------------------------------------------------ scene view

        static Color PointColor(TankRigBuilder.PointKind k) => k switch
        {
            TankRigBuilder.PointKind.Muzzle => new Color(1f, 0.45f, 0.2f),
            TankRigBuilder.PointKind.MachineGun => new Color(1f, 0.85f, 0.2f),
            TankRigBuilder.PointKind.Exhaust => new Color(0.75f, 0.75f, 0.75f),
            TankRigBuilder.PointKind.Headlight => new Color(0.5f, 0.85f, 1f),
            _ => new Color(1f, 0.25f, 0.3f),
        };

        void OnSceneGUI(SceneView sv)
        {
            if (!setup || EditorApplication.isPlaying) return;
            var root = setup.transform;
            float size = HandleUtility.GetHandleSize(root.position);

            // front arrow
            Handles.color = new Color(0.2f, 0.8f, 1f, 0.9f);
            Handles.ArrowHandleCap(0, root.position, Quaternion.LookRotation(root.forward, root.up), size * 1.5f, EventType.Repaint);
            Handles.Label(root.position + root.forward * size * 1.6f, "前");

            // tracks: move the left one, the right one follows mirrored
            if (setup.trackLeft)
            {
                Handles.Label(setup.trackLeft.position + root.up * size * 0.6f, "履帯（左）\n右は左右対称");
                if (setup.trackRight) Handles.Label(setup.trackRight.position + root.up * size * 0.6f, "履帯（右）");
                if (focus == FocusMode.Track)
                {
                    EditorGUI.BeginChangeCheck();
                    Vector3 p = Handles.PositionHandle(setup.trackLeft.position, root.rotation);
                    if (EditorGUI.EndChangeCheck())
                    {
                        Undo.RecordObject(setup, "履帯の移動");
                        Undo.RecordObject(setup.trackLeft, "履帯の移動");
                        if (setup.trackRight) Undo.RecordObject(setup.trackRight, "履帯の移動");
                        var l = root.InverseTransformPoint(p);
                        setup.trackOffset = new Vector3(Mathf.Max(0.02f, -l.x), l.y, l.z);
                        setup.ApplyTrackPose();
                        Repaint();
                    }
                }
                else
                {
                    Handles.color = new Color(0.3f, 1f, 0.6f);
                    float hs = HandleUtility.GetHandleSize(setup.trackLeft.position) * 0.15f;
                    if (Handles.Button(setup.trackLeft.position, root.rotation, hs, hs, Handles.CubeHandleCap)) { focus = FocusMode.Track; Repaint(); }
                }
            }

            // torsion arms: pivot, arm at rest, and the arc the wheel travels (compressed ↔ dropped)
            if (showArms && setup.suspension)
            {
                foreach (var tr in new[] { setup.trackLeft, setup.trackRight })
                {
                    var tt = tr ? tr.GetComponent<TankTrack>() : null;
                    if (!tt) continue;
                    for (int i = 0; i < tt.wheels.Count; i++)
                    {
                        if (!tt.TryGetArm(i, out var pivot, out var rest, out var dropped, out var compressed, out var axle)) continue;
                        Handles.color = new Color(1f, 0.8f, 0.2f, 0.9f);
                        Handles.DrawLine(pivot, rest, 2f);
                        Handles.SphereHandleCap(0, pivot, Quaternion.identity, HandleUtility.GetHandleSize(pivot) * 0.06f, EventType.Repaint);
                        Vector3 from = compressed - pivot;
                        float arc = Vector3.SignedAngle(from, dropped - pivot, axle);
                        Handles.color = new Color(0.3f, 1f, 0.5f, 0.9f);
                        Handles.DrawWireArc(pivot, axle, from, arc, from.magnitude, 2f);
                    }
                }
            }

            // points
            foreach (var k in TankRigBuilder.AllPoints)
            {
                var t = TankRigBuilder.GetPoint(setup, k);
                if (!t) continue;
                float hs = HandleUtility.GetHandleSize(t.position);
                Handles.color = PointColor(k);
                Handles.DrawLine(t.position, t.position + t.forward * hs * 0.9f, 2f);
                Handles.Label(t.position + root.up * hs * 0.25f, TankRigBuilder.Label(k));
                var mirror = TankRigBuilder.GetMirror(setup, k);
                if (mirror && TankRigBuilder.IsMirrored(setup, k))
                {
                    Handles.SphereHandleCap(0, mirror.position, Quaternion.identity, hs * 0.1f, EventType.Repaint);
                    Handles.DrawLine(mirror.position, mirror.position + mirror.forward * hs * 0.9f, 2f);
                }

                if (focus == FocusMode.Point && focusPoint == k)
                {
                    if (Tools.current == Tool.Rotate)
                    {
                        EditorGUI.BeginChangeCheck();
                        var r = Handles.RotationHandle(t.rotation, t.position);
                        if (EditorGUI.EndChangeCheck())
                        {
                            Undo.RecordObject(t, "ポイントの回転");
                            if (TankRigBuilder.GetMirror(setup, k)) Undo.RecordObject(TankRigBuilder.GetMirror(setup, k), "ポイントの回転");
                            t.rotation = r;
                            setup.SyncMirrors();
                        }
                    }
                    else
                    {
                        EditorGUI.BeginChangeCheck();
                        var p = Handles.PositionHandle(t.position, Tools.pivotRotation == PivotRotation.Local ? t.rotation : root.rotation);
                        if (EditorGUI.EndChangeCheck())
                        {
                            Undo.RecordObject(t, "ポイントの移動");
                            if (TankRigBuilder.GetMirror(setup, k)) Undo.RecordObject(TankRigBuilder.GetMirror(setup, k), "ポイントの移動");
                            t.position = p;
                            setup.SyncMirrors();
                        }
                    }
                }
                else if (Handles.Button(t.position, Quaternion.identity, hs * 0.12f, hs * 0.14f, Handles.SphereHandleCap))
                {
                    focus = FocusMode.Point;
                    focusPoint = k;
                    Repaint();
                }
            }

            if (placing.HasValue) HandlePlacing(sv);
        }

        void HandlePlacing(SceneView sv)
        {
            var e = Event.current;
            int id = GUIUtility.GetControlID(FocusType.Passive);
            HandleUtility.AddDefaultControl(id);
            Handles.BeginGUI();
            var r = new Rect(e.mousePosition.x + 14f, e.mousePosition.y + 4f, 260f, 22f);
            GUI.Box(r, "クリックして配置: " + TankRigBuilder.Label(placing.Value) + "（Esc で中止）");
            Handles.EndGUI();
            if (e.type == EventType.MouseMove) sv.Repaint();
            if (e.type == EventType.KeyDown && e.keyCode == KeyCode.Escape)
            {
                placing = null;
                e.Use();
                Repaint();
                return;
            }
            if (e.type == EventType.MouseDown && e.button == 0 && !e.alt)
            {
                var ray = HandleUtility.GUIPointToWorldRay(e.mousePosition);
                var filters = setup.model ? setup.model.GetComponentsInChildren<MeshFilter>() : setup.GetComponentsInChildren<MeshFilter>();
                if (MeshRaycast.Raycast(ray, filters, out var hit))
                {
                    var k = placing.Value;
                    TankRigBuilder.PlacePoint(setup, k, hit.point + hit.normal * 0.03f);
                    focus = FocusMode.Point;
                    focusPoint = k;
                    placing = null;
                    status = TankRigBuilder.Label(k) + "を置きました。";
                    Repaint();
                }
                e.Use();
            }
        }
    }
}
