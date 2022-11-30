using System.Collections.Generic;
using System.Linq;
using GameContent.Scripts.FX.Decals;
using UnityEditor;
using UnityEditor.EditorTools;
using UnityEditor.Experimental.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace GameContent.Scripts.Editor
{
    [EditorTool("Paint PhysicalMaterial")]
    public class PaintPhysicMaterialTool : EditorTool
    {
        private const string PATH = "Assets/GameContent/ObjectDepot/ScriptableObjects/PhysicMaterials";
        private static Vector2 WindowOffset = new Vector2(20, 20);
        private static Vector2 WindowSize = new Vector2(200, 700);

        public static PaintPhysicMaterialTool active;

        [SerializeField] private float MaxDistance = 50;
        [SerializeField] private float Alpha = .25f;
        [SerializeField] private bool DrawAll;
        [SerializeField] private bool UseLeftClick = true;
        [SerializeField] private KeyCode paintHotKey = KeyCode.None;
        [SerializeField] private PhysicMaterial current;
        [SerializeField] private bool drawSettings;
        private Vector2 scroll;
        private List<PhysicMaterial> physicMaterials = new List<PhysicMaterial>();

        private RCC_GroundMaterials rccGroundMaterials => RCC_GroundMaterials.Instance;

        protected void OnEnable()
        {
            ToolManager.activeToolChanging -= OnActiveToolWillChange;
            ToolManager.activeToolChanging += OnActiveToolWillChange;
            ToolManager.activeToolChanged -= OnActiveToolDidChange;
            ToolManager.activeToolChanged += OnActiveToolDidChange;
            RCC_GroundMaterials.Load();
        }

        void OnSceneGUI(SceneView sceneView)
        {
            var windowRect = sceneView.position;
            var newRect = windowRect;
            newRect.xMin = newRect.xMax - WindowSize.x;
            newRect.yMin = newRect.yMax - WindowSize.y;
            newRect.xMax -= WindowOffset.x;
            newRect.yMax -= WindowOffset.y;

            Scene scene = default;

            var stage = PrefabStageUtility.GetCurrentPrefabStage();
            if (stage)
            {
                scene = stage.scene;
            }
            else
            {
                scene = SceneManager.GetActiveScene();
            }

            if (DrawAll)
            {
                DrawAllColliders(scene, sceneView);
            }

            DrawColliderCurrent(scene);

            Handles.BeginGUI();

            EditorGUI.DrawRect(newRect, Color.black / 4);
            GUILayout.BeginArea(newRect);

            MaxDistance = EditorGUILayout.Slider(MaxDistance, 0, 1000);
            Alpha = EditorGUILayout.Slider(Alpha, 0, 1);
            DrawAll = EditorGUILayout.Toggle(nameof(DrawAll), DrawAll);
            UseLeftClick = EditorGUILayout.Toggle(nameof(UseLeftClick), UseLeftClick);

            EditorGUI.BeginChangeCheck();

            KeyCode newKey = (KeyCode) EditorGUILayout.EnumPopup(paintHotKey);

            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(this, "paintHotKey");
                paintHotKey = newKey;
            }

            EditorGUILayout.ObjectField(current, typeof(PhysicMaterial), false);

            scroll = GUILayout.BeginScrollView(scroll, GUILayout.MaxHeight(WindowSize.y - 60));

            foreach (var material in physicMaterials)
            {
                DrawMaterialToggle(material);
            }

            GUILayout.EndScrollView();

            GUILayout.EndArea();
            Handles.EndGUI();

            bool isClicked = UseLeftClick && Event.current.type == EventType.MouseDown && Event.current.button == 0 &&
                             Event.current.isMouse;

            bool isHotkey = paintHotKey != KeyCode.None && Event.current.type == EventType.KeyDown &&
                            Event.current.keyCode == paintHotKey &&
                            Event.current.isKey;

            if (isHotkey || isClicked)
            {
                if (isClicked)
                {
                    GUIUtility.hotControl = -1;
                    Event.current.Use();
                }

                SwapMaterialUnderCursor(scene);
            }

            sceneView.Repaint();
        }

        private void DrawAllColliders(Scene scene, SceneView sceneView)
        {
            if (Alpha == 0) return;

            Vector3 cameraPosition = sceneView.camera.transform.position;

            var planes = GeometryUtility.CalculateFrustumPlanes(sceneView.camera);

            var colliders = scene.GetRootGameObjects().SelectMany(go => go.GetComponentsInChildren<Collider>())
                .ToArray();

            foreach (Collider collider in colliders)
            {
                if ((collider.bounds.center - cameraPosition).sqrMagnitude < MaxDistance &&
                    GeometryUtility.TestPlanesAABB(planes, collider.bounds))
                {
                    DrawCollider(collider, Alpha);
                }
            }
        }

        private void DrawColliderCurrent(Scene scene)
        {
            Ray ray = HandleUtility.GUIPointToWorldRay(Event.current.mousePosition);

            var result = scene.GetPhysicsScene().Raycast(ray.origin, ray.direction, out var hit, 1000);

            if (!result) return;

            DrawCollider(hit.collider, 1);
        }

        private void DrawCollider(Collider collider, float alpha)
        {
            if (!(collider is BoxCollider boxCollider)) return;

            Handles.matrix = boxCollider.transform.localToWorldMatrix;

            var mat = RCC_GroundMaterials.GetSurfaceData(boxCollider.sharedMaterial);

            Handles.color = DecalFXSubHolder.GetColor(mat.matType).SetAlpha(alpha);

            Handles.DrawWireCube(boxCollider.center, boxCollider.size);
            Handles.matrix = default;
        }

        private void SwapMaterialUnderCursor(Scene scene)
        {
            Debug.Log($"click Swap material");

            Ray ray = HandleUtility.GUIPointToWorldRay(Event.current.mousePosition);

            var result = scene.GetPhysicsScene().Raycast(ray.origin, ray.direction, out var hit, 1000);
            if (result)
            {
                Debug.Log($"Swap material '{hit.collider.name}'", hit.collider);
                SwapMaterial(hit.collider);
            }
        }

        void SwapMaterial(Collider collider)
        {
            Undo.RecordObject(collider, $"swap material {current?.name}");
            collider.sharedMaterial = current;
        }

        void DrawMaterialToggle(PhysicMaterial material)
        {
            bool newResult = GUILayout.Toggle(material == current, material?.name, "button");
            if (newResult)
            {
                current = material;
            }
        }

        public void Init()
        {
            SceneView.duringSceneGui -= OnSceneGUI;
            SceneView.duringSceneGui += OnSceneGUI;

            active = this;

            physicMaterials = AssetDatabase.FindAssets($"t:{nameof(PhysicMaterial)}", new[] { PATH })
                .Select(AssetDatabase.GUIDToAssetPath)
                .Select(AssetDatabase.LoadAssetAtPath<PhysicMaterial>).ToList();

            physicMaterials.Insert(0, null);
            EditorPrefs.SetString("PrefferedTool", typeof(PaintPhysicMaterialTool).FullName);
        }

        void OnActiveToolWillChange()
        {
            if (ToolManager.IsActiveTool(this))
            {
                Init();
            }
        }

        void OnActiveToolDidChange()
        {
            if (ToolManager.IsActiveTool(this))
            {
                Init();
            }
            else
            {
                SceneView.duringSceneGui -= OnSceneGUI;
            }
        }
    }
}
