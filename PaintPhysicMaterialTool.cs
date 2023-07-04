using System.Collections.Generic;
using System.Linq;
using GameContent.Scripts.FX.Decals;
using Plugins.Editor.SceneViews;
using UnityEditor;
using UnityEditor.EditorTools;
using UnityEditor.Experimental.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace GameContent.Scripts.Editor
{
	/// Allow to display or paint Physical Material by mouse pointer and hotkeys
	[EditorTool("Paint PhysicalMaterial")]
	public class PaintPhysicMaterialTool : EditorTool
	{
		private const string PATH = "Assets/GameContent/ObjectDepot/ScriptableObjects/PhysicMaterials";

		public static PaintPhysicMaterialTool active;

		[SerializeField] private Texture2D _icon;
		[SerializeField] private float MaxDistance = 50;
		[SerializeField] private float Alpha = .25f;
		[SerializeField] private bool DrawAll;
		[SerializeField] private bool UseLeftClick = true;
		[SerializeField] private KeyCode paintHotKey = KeyCode.None;
		[SerializeField] private PhysicMaterial current;
		[SerializeField] private bool drawSettings;
		[SerializeField] private Vector2 WindowOffset = new Vector2(20, 20);
		[SerializeField] private Vector2 WindowSize = new Vector2(200, 460);
		private Vector2 scroll;
		private Shader MeshColliderShader;
		private Material wireMat;
		private GUIContent _guiContent;
		private List<PhysicMaterial> physicMaterials = new List<PhysicMaterial>();

		private RCC_GroundMaterials rccGroundMaterials => RCC_GroundMaterials.Instance;

		public override GUIContent toolbarIcon => _guiContent;

		private OverlayWindowContainer _window;
		private static readonly int Color = Shader.PropertyToID("_Color");

		protected void OnEnable()
		{
			_guiContent = new GUIContent("Paint Physical Material", _icon, "Allow to display or paint Physical Material by mouse pointer and hotkeys");
			ToolManager.activeToolChanging -= OnActiveToolWillChange;
			ToolManager.activeToolChanging += OnActiveToolWillChange;
			ToolManager.activeToolChanged -= OnActiveToolDidChange;
			ToolManager.activeToolChanged += OnActiveToolDidChange;
			RCC_GroundMaterials.Load();
			_window = new OverlayWindowContainer(_guiContent, OnWindowGUI, -1000, this, OverlayWindowContainer.WindowDisplayOption.OneWindowPerTitle);
		}

		public override void OnToolGUI(EditorWindow sceneWindow)
		{
			// Window must be SceneView
			if (!(sceneWindow is SceneView sceneView)) return;

			PrefabStage stage = PrefabStageUtility.GetCurrentPrefabStage();
			Scene scene = stage ? stage.scene : SceneManager.GetActiveScene();
			if (Event.current.type == EventType.Repaint)
			{
				if (DrawAll) DrawAllColliders(scene, sceneView);
				DrawColliderCurrent(scene);
				sceneView.Repaint();
			}

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

			_window.OnGUI();
		}

		private void OnWindowGUI(object target, SceneView sceneView)
		{
			Handles.BeginGUI();

			EditorGUILayout.ObjectField(MonoScript.FromScriptableObject(this), typeof(MonoScript), true);

			float lastLabelWidth = EditorGUIUtility.labelWidth;
			EditorGUIUtility.labelWidth = 100;

			MaxDistance = EditorGUILayout.Slider("Max Distance", MaxDistance, 0, 1000);
			Alpha = EditorGUILayout.Slider(nameof(Alpha), Alpha, 0, 1);
			DrawAll = EditorGUILayout.Toggle("Draw All", DrawAll);
			UseLeftClick = EditorGUILayout.Toggle("Use Left Click", UseLeftClick);

			EditorGUI.BeginChangeCheck();

			KeyCode newKey = (KeyCode)EditorGUILayout.EnumPopup("Paint", paintHotKey);

			if (EditorGUI.EndChangeCheck())
			{
				Undo.RecordObject(this, "paintHotKey");
				paintHotKey = newKey;
			}

			EditorGUILayout.ObjectField(current, typeof(PhysicMaterial), false);

			scroll = GUILayout.BeginScrollView(scroll, GUILayout.MaxHeight(WindowSize.y - 60));

			foreach (PhysicMaterial material in physicMaterials)
			{
				DrawMaterialToggle(material);
			}

			GUILayout.EndScrollView();
			EditorGUIUtility.labelWidth = lastLabelWidth;
			Handles.EndGUI();
		}

		private void DrawAllColliders(Scene scene, SceneView sceneView)
		{
			if (Alpha == 0) return;

			Vector3 cameraPosition = sceneView.camera.transform.position;

			Plane[] planes = GeometryUtility.CalculateFrustumPlanes(sceneView.camera);

			Collider[] colliders = scene.GetRootGameObjects().SelectMany(go => go.GetComponentsInChildren<Collider>())
				.ToArray();

			foreach (Collider collider in colliders)
			{
				if ((collider.bounds.center - cameraPosition).sqrMagnitude < MaxDistance * MaxDistance &&
				    GeometryUtility.TestPlanesAABB(planes, collider.bounds))
				{
					DrawCollider(collider, Alpha);
				}
			}
		}

		private void DrawColliderCurrent(Scene scene)
		{
			Ray ray = HandleUtility.GUIPointToWorldRay(Event.current.mousePosition);

			bool result = scene.GetPhysicsScene().Raycast(ray.origin, ray.direction, out RaycastHit hit, 1000);

			if (!result) return;

			DrawCollider(hit.collider, 1);
		}

		private void DrawCollider(Collider collider, float alpha)
		{
			if (collider.isTrigger) return;
			if (!collider.enabled) return;

			switch (collider)
			{
				case BoxCollider box:
				{
					Matrix4x4 old = Handles.matrix;
					Handles.matrix = box.transform.localToWorldMatrix;

					RCC_GroundMaterials.GroundMaterialFrictions mat = RCC_GroundMaterials.GetSurfaceData(box.sharedMaterial);

					Handles.color = DecalFXSubHolder.GetColor(mat.matType).SetAlpha(alpha);

					Handles.DrawWireCube(box.center, box.size);
					Handles.matrix = old;
					break;
				}

				case CapsuleCollider capsule:
				{
					RCC_GroundMaterials.GroundMaterialFrictions mat = RCC_GroundMaterials.GetSurfaceData(capsule.sharedMaterial);
					DrawCapsule(capsule, DecalFXSubHolder.GetColor(mat.matType).SetAlpha(alpha));
					break;
				}

				case SphereCollider sphere:
				{
					RCC_GroundMaterials.GroundMaterialFrictions mat = RCC_GroundMaterials.GetSurfaceData(sphere.sharedMaterial);
					DrawSphere(sphere, DecalFXSubHolder.GetColor(mat.matType).SetAlpha(alpha));
					break;
				}

				case MeshCollider mesh:
				{
					RCC_GroundMaterials.GroundMaterialFrictions mat = RCC_GroundMaterials.GetSurfaceData(mesh.sharedMaterial);
					DrawMesh(mesh, DecalFXSubHolder.GetColor(mat.matType).SetAlpha(alpha));
					break;
				}
			}
		}

		private void SwapMaterialUnderCursor(Scene scene)
		{
			Debug.Log($"click Swap material");

			Ray ray = HandleUtility.GUIPointToWorldRay(Event.current.mousePosition);

			bool result = scene.GetPhysicsScene().Raycast(ray.origin, ray.direction, out RaycastHit hit, 1000);
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
			if (material == null) return;

			bool newResult = GUILayout.Toggle(material == current, material.name, "button");
			if (newResult)
			{
				current = material;
			}
		}

		public void Init()
		{
			// SceneView.duringSceneGui -= OnWindowGUI;
			// SceneView.duringSceneGui += OnWindowGUI;

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
				//SceneView.duringSceneGui -= OnWindowGUI;
			}
		}

		private void DrawCapsule(CapsuleCollider capsule, Color color)
		{
			Handles.color = color;
			// calculate top and bottom center sphere locations.
			float offset = capsule.height / 2;
			Vector3 top, bottom = top = capsule.center;
			float radius = capsule.radius;
#if (UNITY_2017_2_OR_NEWER)
			Vector3 scale = capsule.transform.localToWorldMatrix.lossyScale;
#else
      Vector3 scale = CurrentAttachTo.transform.lossyScale;
#endif
			switch (capsule.direction)
			{
				case 0: //x axis
					//adjust radius by the bigger scale.
					radius *= scale.y > scale.z ? scale.y : scale.z;
					// adjust the offset to top and bottom mid points for spheres based on radius / scale in that direction
					offset -= radius / scale.x;
					// offset top and bottom points.
					top.x += offset;
					bottom.x -= offset;
					break;

				case 1:
					radius *= scale.x > scale.z ? scale.x : scale.z;
					offset -= radius / scale.y;
					top.y += offset;
					bottom.y -= offset;
					break;

				case 2:
					radius *= scale.x > scale.y ? scale.x : scale.y;
					offset -= radius / scale.z;
					top.z += offset;
					bottom.z -= offset;
					break;
			}

			if (capsule.height < capsule.radius * 2)
			{
				// draw just the sphere if the radius and the height will make a sphere.
				Vector3 worldCenter = capsule.transform.localToWorldMatrix.MultiplyPoint(capsule.center);
				Handles.DrawWireDisc(worldCenter, Vector3.forward, radius);
				Handles.DrawWireDisc(worldCenter, Vector3.right, radius);
				Handles.DrawWireDisc(worldCenter, Vector3.up, radius);
				return;
			}

			Vector3 worldTop = capsule.transform.localToWorldMatrix.MultiplyPoint3x4(top);
			Vector3 worldBottom = capsule.transform.localToWorldMatrix.MultiplyPoint3x4(bottom);
			Vector3 up = worldTop - worldBottom;
			Vector3 cross1 = Vector3.up;
			// dont want to cross if in same direction, forward works in this case as the first cross
			if (up.normalized == cross1 || up.normalized == -cross1)
			{
				cross1 = Vector3.forward;
			}

			Vector3 right = Vector3.Cross(up, -cross1).normalized;
			Vector3 forward = Vector3.Cross(up, -right).normalized;
			// full circles at top and bottom
			Handles.DrawWireDisc(worldTop, up, radius);
			Handles.DrawWireDisc(worldBottom, up, radius);
			// half arcs at top and bottom
			Handles.DrawWireArc(worldTop, forward, right, 180f, radius);
			Handles.DrawWireArc(worldTop, -right, forward, 180f, radius);
			Handles.DrawWireArc(worldBottom, -forward, right, 180f, radius);
			Handles.DrawWireArc(worldBottom, right, forward, 180f, radius);
			// connect bottom and top side points
			Handles.DrawLine(worldTop + right * radius, worldBottom + right * radius);
			Handles.DrawLine(worldTop - right * radius, worldBottom - right * radius);
			Handles.DrawLine(worldTop + forward * radius, worldBottom + forward * radius);
			Handles.DrawLine(worldTop - forward * radius, worldBottom - forward * radius);
		}

		private void DrawSphere(SphereCollider sphere, Color color)
		{
			Handles.color = color;
			Vector3 worldCenter = sphere.transform.localToWorldMatrix.MultiplyPoint3x4(sphere.center);
			// Draw all normal axis' rings at the world center location for both perspective and isometric/orthographic
			float radius = sphere.radius;
#if (UNITY_2017_2_OR_NEWER)
			Vector3 scale = sphere.transform.localToWorldMatrix.lossyScale;
#else
      Vector3 scale = CurrentAttachTo.transform.lossyScale;
#endif
			float largestScale = Mathf.Max(Mathf.Max(scale.x, scale.y), scale.z);
			radius *= largestScale;
			Handles.DrawWireDisc(worldCenter, Vector3.forward, radius);
			Handles.DrawWireDisc(worldCenter, Vector3.right, radius);
			Handles.DrawWireDisc(worldCenter, Vector3.up, radius);
			// orthographic camera
			if (Camera.current != null)
			{
				if (Camera.current.orthographic)
				{
					// simple, use cameras forward in orthographic
					Handles.DrawWireDisc(worldCenter, Camera.current.transform.forward, radius);
				}
				else
				{
					// draw a circle facing the camera covering all the radius in prespective mode
					Vector3 normal = worldCenter - Handles.inverseMatrix.MultiplyPoint(Camera.current.transform.position);
					float sqrMagnitude = normal.sqrMagnitude;
					float r2 = radius * radius;
					float r4m = r2 * r2 / sqrMagnitude;
					float newRadius = Mathf.Sqrt(r2 - r4m);
					Handles.DrawWireDisc(worldCenter - r2 * normal / sqrMagnitude, normal, newRadius);
				}
			}
		}

		private void DrawMesh(MeshCollider mesh, Color color)
		{
			// try to find mesh shader
			if (MeshColliderShader == null) MeshColliderShader = Shader.Find("Custom/EasyColliderMeshColliderPreview");
			if (!wireMat) wireMat = new Material(MeshColliderShader);
			if (MeshColliderShader == null || mesh.sharedMesh == null) return;

			wireMat.SetColor(Color, color);
			wireMat.SetPass(0);
			GL.wireframe = true;
			Graphics.DrawMeshNow(mesh.sharedMesh, mesh.transform.localToWorldMatrix);
			GL.wireframe = false;
			// Graphics.DrawMeshNow(mesh.sharedMesh, mesh.transform.localToWorldMatrix);
		}
	}
}
