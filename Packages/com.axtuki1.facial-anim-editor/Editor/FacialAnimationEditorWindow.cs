using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace AX.FacialAnimationEditor
{
    public class FacialAnimationEditorWindow : EditorWindow, IHasCustomMenu
    {

        private static readonly string packagePath = "Packages/com.axtuki1.facial-anim-editor/Editor";

        private List<BlendshapeItem> items = new List<BlendshapeItem>();
        private List<BlendshapeItem> filteredItems = new List<BlendshapeItem>();
        private List<BlendshapeItem> targetedItems = new List<BlendshapeItem>();

        private ObjectField objectRoot, targetMesh, targetAnimation;
        private VisualElement selected;
        private VisualElement nonSelected;
        private ListView blendshapeList, targetBlendshapeList;
        private IMGUIContainer previewContainer;
        private Slider fov;
        private Vector3Field offset;
        private VisualTreeAsset blendshapeListItem;
        private ToolbarSearchField blendshapeSearch, targetBlendshapeSearch;
        private Button animationLoadBtn, animationSaveBtn, animationSaveNewFileBtn, resetBtn;

        private PreviewRenderUtility previewUtility;
        private GameObject previewObjectRoot;
        private GameObject previewMeshObject;

        private string relativePathRootToTargetMeshInPreview;

        private float previewCameraDistance = 2f;
        private float previewCameraFov = 3f;
        private Vector3 previewCameraPosOffset = new Vector3(0, 0.4f, 0);

        public void AddItemsToMenu(GenericMenu menu)
        {
            // メニューアイテムを登録。
            menu.AddItem(new GUIContent("Reload"), false, () => { GUIReload(); });
        }

        [MenuItem("Window/Facial Animation Editor")]
        public static void ShowWindow()
        {
            var window = GetWindow<FacialAnimationEditorWindow>("Facial Animation Editor");
            window.minSize = new Vector2(890, 650);
        }

        public void OnEnable()
        {
            GUIReload();
            items.Clear();
            previewUtility = new PreviewRenderUtility();
            previewUtility.cameraFieldOfView = previewCameraFov;
            previewUtility.camera.nearClipPlane = 0.1f;
            previewUtility.camera.farClipPlane = 10f;
        }

        public void OnDisable()
        {
            if (previewObjectRoot != null)
                DestroyImmediate(previewObjectRoot);
            previewUtility.Cleanup();
        }

        private void GUIReload()
        {
            rootVisualElement.Clear();

            var rootTree =
                AssetDatabase.LoadAssetAtPath<VisualTreeAsset>($"{packagePath}/FacialAnimationEditorWindow.uxml");
            rootTree.CloneTree(rootVisualElement);

            // オブジェクトキャッシュ
            objectRoot = rootVisualElement.Q<ObjectField>("AnimationRoot");
            objectRoot.RegisterValueChangedCallback(evt =>
            {
                var oldRoot = (GameObject)objectRoot.value;
                if (previewObjectRoot != null)
                {
                    DestroyImmediate(previewObjectRoot);
                }
                if (oldRoot == null)
                {
                    targetMesh.value = null;
                    items.Clear();
                    filteredItems.Clear();
                    targetedItems.Clear();
                    blendshapeList.RefreshItems();
                    targetBlendshapeList.RefreshItems();
                    viewUpdate();
                    return;
                }
                previewObjectRoot = Instantiate(oldRoot);
                previewUtility.AddSingleGO(previewObjectRoot);
                viewUpdate();
            });

            nonSelected = rootVisualElement.Q<VisualElement>("NonAnimationRootSelectedArea");
            selected = rootVisualElement.Q<VisualElement>("SelectedArea");
            
            targetAnimation = rootVisualElement.Q<ObjectField>("TargetAnimation");

            targetMesh = rootVisualElement.Q<ObjectField>("TargetMesh");
            targetMesh.RegisterValueChangedCallback(evt =>
            {
                if (targetMesh.value == null)
                {
                    previewMeshObject = null;
                    items.Clear();
                    filteredItems.Clear();
                    targetedItems.Clear();
                    blendshapeList.RefreshItems();
                    targetBlendshapeList.RefreshItems();
                    viewUpdate();
                    return;
                }

                relativePathRootToTargetMeshInPreview = GetRelativePath(
                    ((GameObject)objectRoot.value).transform,
                    ((SkinnedMeshRenderer)targetMesh.value).transform
                );
                previewMeshObject = FindByRelativePath(
                    previewObjectRoot.transform,
                    relativePathRootToTargetMeshInPreview
                ).gameObject;

                items.Clear();
                filteredItems.Clear();
                targetedItems.Clear();
                RegisterBlendshapeList();
                blendshapeList.RefreshItems();
                targetBlendshapeList.RefreshItems();
                viewUpdate();
            });

            blendshapeList = rootVisualElement.Q<ListView>("BlendshapeList");

            blendshapeList.itemsSource = filteredItems;

            blendshapeList.makeItem = () => new BlendshapeItemUI(this, blendshapeListItem);

            blendshapeList.bindItem = (element, i) =>
            {
                var index = i;
                var item = items[index];
                var itemUI = element as BlendshapeItemUI;
                itemUI?.Setup(item, index);
            };

            blendshapeListItem =
                AssetDatabase.LoadAssetAtPath<VisualTreeAsset>($"{packagePath}/BlendshapeListItem.uxml");
            
            targetBlendshapeList = rootVisualElement.Q<ListView>("TargetBlendshapeList");
            
            targetBlendshapeList.itemsSource = targetedItems;
            
            targetBlendshapeList.makeItem = () => new BlendshapeItemUI(this, blendshapeListItem);
            
            targetBlendshapeList.bindItem = (element, i) =>
            {
                var index = i;
                var item = targetedItems[index];
                var itemUI = element as BlendshapeItemUI;
                itemUI?.Setup(item, index);
            };

            previewContainer = rootVisualElement.Q<IMGUIContainer>("PreviewContainer");

            previewContainer.onGUIHandler = () =>
            {

                previewUtility.cameraFieldOfView = previewCameraFov;

                if (previewMeshObject == null)
                {
                    DrawEmptyPreview(previewContainer.contentRect);
                    return;
                }

                var renderer = previewMeshObject.GetComponent<SkinnedMeshRenderer>();

                if (renderer == null)
                {
                    DrawEmptyPreview(previewContainer.contentRect);
                    return;
                }

                previewUtility.BeginPreview(
                    new Rect(
                        previewContainer.contentRect.x,
                        previewContainer.contentRect.y,
                        previewContainer.contentRect.width * 2,
                        previewContainer.contentRect.height * 2
                    ),
                    GUIStyle.none
                );

                var cam = previewUtility.camera;
                cam.transform.position =
                    renderer.bounds.center + new Vector3(0, 0, renderer.bounds.extents.magnitude * 2);
                cam.transform.LookAt(renderer.bounds.center);
                cam.transform.position += previewCameraPosOffset;
                cam.Render();

                Texture resultRender = previewUtility.EndPreview();
                GUI.DrawTexture(previewContainer.contentRect, resultRender, ScaleMode.ScaleToFit);
            };

            blendshapeSearch = rootVisualElement.Q<ToolbarSearchField>("BlendshapeSearch");

            blendshapeSearch.RegisterValueChangedCallback(evt =>
            {
                ApplyBlendshapeFilter();
            });
            
            targetBlendshapeSearch = rootVisualElement.Q<ToolbarSearchField>("TargetBlendshapeSearch");

            targetBlendshapeSearch.RegisterValueChangedCallback(evt =>
            {
                ApplyBlendshapeFilter();
            });

            animationLoadBtn = rootVisualElement.Q<Button>("AnimationLoadBtn");
            
            animationLoadBtn.RegisterCallback<ClickEvent>(AnimationLoadBtnClick);
            
            animationSaveBtn = rootVisualElement.Q<Button>("AnimationSaveBtn");

            animationSaveBtn.RegisterCallback<ClickEvent>(AnimationSaveBtnClick);

            animationSaveNewFileBtn = rootVisualElement.Q<Button>("AnimationSaveNewFileBtn");
            
            animationSaveNewFileBtn.RegisterCallback<ClickEvent>(AnimationSaveNewFileBtnClick);

            offset = rootVisualElement.Q<Vector3Field>("PreviewCamOffset");
            offset.RegisterValueChangedCallback(evt => { previewCameraPosOffset = evt.newValue; });
            offset.SetValueWithoutNotify(previewCameraPosOffset);

            fov = rootVisualElement.Q<Slider>("PreviewCamFOV");
            fov.RegisterValueChangedCallback(evt => { previewCameraFov = evt.newValue; });
            fov.SetValueWithoutNotify(previewCameraFov);
            
            resetBtn = rootVisualElement.Q<Button>("ResetBtn");
            
            resetBtn.RegisterCallback<ClickEvent>(evt =>
            {
                AllItemsReset();
            });

            viewUpdate();
        }

        internal void AllItemsReset()
        {
            for (int i = 0; i < items.Count; i++)
            {
                var item = items[i];
                item.weight = item.defaultWeight;
                item.isSelected = false;
            }
            ApplyBlendshapeFilter();
        }

        internal void ApplyBlendshapeFilter()
        {
            string keyword = blendshapeSearch.value.Trim().ToLower();
            string targetKeyword = targetBlendshapeSearch.value.Trim().ToLower();
    
            filteredItems.Clear();
            targetedItems.Clear();

            for (int i = 0; i < items.Count; i++)
            {
                var item = items[i];
                if (string.IsNullOrEmpty(keyword) || item.name.ToLower().Contains(keyword))
                {
                    filteredItems.Add(item);
                }
                if ((string.IsNullOrEmpty(targetKeyword) || item.name.ToLower().Contains(targetKeyword)) && item.isSelected)
                {
                    targetedItems.Add(item);
                }
                UpdateBlendshape(i);
            }

            blendshapeList.RefreshItems();
            targetBlendshapeList.RefreshItems();
        }

        internal void UpdateBlendshape(int i)
        {
            var item = items[i];
            if (previewMeshObject != null)
            {
                var renderer = previewMeshObject.GetComponent<SkinnedMeshRenderer>();
                if (renderer != null)
                {
                    if (item.isSelected)
                    {
                        renderer.SetBlendShapeWeight(i, item.weight);
                    }
                    else
                    {
                        renderer.SetBlendShapeWeight(i, item.defaultWeight);
                    }
                }
            }
        }

        private void DrawEmptyPreview(Rect rect)
        {
            GUIStyle style = new GUIStyle(GUI.skin.box);
            style.alignment = TextAnchor.MiddleCenter;
            style.normal.textColor = Color.white;
            GUI.Box(rect, "( ˘ω˘)ｽﾔｧ...", style);
        }

        private void viewUpdate()
        {

            if (objectRoot.value != null)
            {
                selected.style.display = DisplayStyle.Flex;
                nonSelected.style.display = DisplayStyle.None;
            }
            else
            {
                selected.style.display = DisplayStyle.None;
                nonSelected.style.display = DisplayStyle.Flex;
            }

        }

        private void RegisterBlendshapeList()
        {
            var mesh = targetMesh.value as SkinnedMeshRenderer;
            if (mesh != null)
            {
                blendshapeList.Clear();
                for (int i = 0; i < mesh.sharedMesh.blendShapeCount; i++)
                {
                    var name = mesh.sharedMesh.GetBlendShapeName(i);
                    if (items.Find(x => x.name == name) != null)
                        continue;
                    var weight = mesh.GetBlendShapeWeight(i);
                    var item = new BlendshapeItem(name, weight);
                    items.Add(item);
                    filteredItems.Add(item);
                    previewMeshObject.GetComponent<SkinnedMeshRenderer>().SetBlendShapeWeight(i, weight);
                }
            }
        }

        private int GetBlendshapeIndex(string name)
        {
            var mesh = (targetMesh.value as SkinnedMeshRenderer)?.sharedMesh;
            if (mesh == null) return -1;
            return mesh.GetBlendShapeIndex(name);
        }

        private string GetRelativePath(Transform root, Transform target)
        {
            if (!target.IsChildOf(root))
                throw new System.ArgumentException("Target is not a child of root");

            var path = new Stack<string>();
            var current = target;

            while (current != root)
            {
                path.Push(current.name);
                current = current.parent;
            }

            return string.Join("/", path);
        }

        private Transform FindByRelativePath(Transform root, string relativePath)
        {
            return root.Find(relativePath); // UnityのTransform.Findはスラッシュ区切りに対応
        }
        
        private void AnimationLoadBtnClick(ClickEvent evt)
        {
            if (targetAnimation.value == null) return;
            AllItemsReset();
            LoadFromClip((AnimationClip)targetAnimation.value);
        }
        
        private void LoadFromClip(AnimationClip clip)
        {
            if (clip == null) return;

            var bindings = AnimationUtility.GetCurveBindings(clip);
            var expectedPath = GetRelativePath(
                ((GameObject)objectRoot.value).transform,
                ((SkinnedMeshRenderer)targetMesh.value).transform
            );
            
            foreach (var binding in bindings)
            {
                if (binding.type != typeof(SkinnedMeshRenderer)) continue;

                if (!binding.propertyName.StartsWith("blendShape.")) continue;
                
                if (binding.path != expectedPath) continue;

                string blendshapeName = binding.propertyName.Replace("blendShape.", "");
                var curve = AnimationUtility.GetEditorCurve(clip, binding);

                if (curve == null || curve.length == 0) continue;

                float value = curve.Evaluate(0f); // or use curve[0].value

                var item = items.FirstOrDefault(x => x.name == blendshapeName);
                if (item != null)
                {
                    item.weight = value;
                    item.isSelected = true;
                }
            }

            ApplyBlendshapeFilter();
        }
        
        private void AnimationSaveBtnClick(ClickEvent evt)
        {
            var clip = targetAnimation.value as AnimationClip;

            if (clip != null)
            {
                var isOverwrite = EditorUtility.DisplayDialog(
                    "アニメーションの上書き",
                    "アニメーションを上書きしますか？",
                    "はい",
                    "いいえ"
                );
                if (isOverwrite)
                {
                    var path = AssetDatabase.GetAssetPath(clip);
                    
                    var newClip = new AnimationClip();
                    ApplyToClip(newClip);

                    AssetDatabase.CreateAsset(newClip, path);
                    AssetDatabase.SaveAssets();
                    AssetDatabase.Refresh();
                    targetAnimation.value = newClip;
                }
            }
            else
            {
                EditorUtility.DisplayDialog("エラー", "AnimationClipが指定されていません", "OK");
            }
        }
        
        private void AnimationSaveNewFileBtnClick(ClickEvent evt)
        {
            string path = EditorUtility.SaveFilePanelInProject(
                "保存先を選択",
                "NewBlendshapeAnim",
                "anim",
                "保存する場所を選んでください");
            if (!string.IsNullOrEmpty(path))
            {
                var newClip = new AnimationClip();
                ApplyToClip(newClip);

                AssetDatabase.CreateAsset(newClip, path);
                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();
                
                targetAnimation.value = newClip;
            }
        }

        
        private void ApplyToClip(AnimationClip clip)
        {
            var mesh = (targetMesh.value as SkinnedMeshRenderer)?.sharedMesh;
            if (mesh == null || clip == null) return;

            // 開始前に Undo 対応しておくと安心（オプション）
            Undo.RegisterCompleteObjectUndo(clip, "Modify Blendshape Animation");

            foreach (var item in items.Where(x => x.isSelected))
            {
                var path = GetRelativePath(((GameObject)objectRoot.value).transform,
                    ((SkinnedMeshRenderer)targetMesh.value).transform);
        
                // 書き出すアニメーションカーブ
                var curve = new AnimationCurve();
                curve.AddKey(new Keyframe(0f, item.weight));
                curve.AddKey(new Keyframe(0.01f, item.weight));

                // `blendShape.<name>` がターゲットプロパティ名！
                clip.SetCurve(path, typeof(SkinnedMeshRenderer), $"blendShape.{item.name}", curve);
            }
            
            // ループ設定
            AnimationClipSettings clipSettings = AnimationUtility.GetAnimationClipSettings(clip);
            clipSettings.loopTime = true;
            AnimationUtility.SetAnimationClipSettings(clip, clipSettings);

            clip.wrapMode = WrapMode.Loop;

            EditorUtility.SetDirty(clip);
            AssetDatabase.SaveAssets();
        }
    }

    public class BlendshapeItem
    {
        public string name;
        public float weight, defaultWeight;
        public bool isSelected;

        public BlendshapeItem(string name, float weight)
        {
            this.name = name;
            this.weight = weight;
            this.defaultWeight = weight;
            isSelected = false;
        }
    }

    public class BlendshapeItemUI : VisualElement
    {
        private Toggle toggle;
        private Slider slider;
        private FloatField inputfield;
        private FacialAnimationEditorWindow window;

        private BlendshapeItem item;
        private int index;

        public void Setup(BlendshapeItem item, int index)
        {
            this.item = item;
            this.index = index;
            
            toggle.SetValueWithoutNotify(item.isSelected);
            slider.label = item.name;
            slider.SetValueWithoutNotify(item.weight);
            inputfield.SetValueWithoutNotify(item.weight);
        }
        
        public BlendshapeItemUI(FacialAnimationEditorWindow window, VisualTreeAsset blendshapeListItem)
        {
            this.window = window;
            blendshapeListItem.CloneTree(this);
            
            toggle = this.Q<Toggle>("BlendshapeEnable");
            slider = this.Q<Slider>("BlendshapeWeight");
            inputfield = this.Q<FloatField>("BlendshapeWeightInput");

            toggle.RegisterValueChangedCallback(evt =>
            {
                item.isSelected = evt.newValue;
                this.window.ApplyBlendshapeFilter();
            });

            slider.RegisterValueChangedCallback(evt =>
            {
                item.weight = evt.newValue;
                item.isSelected = true;
                toggle.SetValueWithoutNotify(item.isSelected);
                inputfield.SetValueWithoutNotify(item.weight);
                this.window.ApplyBlendshapeFilter();
            });

            inputfield.RegisterValueChangedCallback(evt =>
            {
                item.weight = evt.newValue;
                item.isSelected = true;
                slider.SetValueWithoutNotify(evt.newValue);
                toggle.SetValueWithoutNotify(item.isSelected);
                this.window.ApplyBlendshapeFilter();
            });
        }

    }
}