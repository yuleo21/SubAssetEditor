using UnityEditor;
using UnityEngine;
using UnityEditor.Animations;
using System.Collections.Generic;
using System.IO;

public class SubAssetEditorWindow : EditorWindow
{
    private Object targetAsset;
    private List<Object> assetsToEmbed = new List<Object>();
    private List<Object> subAssets = new List<Object>();
    private Vector2 scrollPosition;
    private Dictionary<Object, bool> extractionSelections = new Dictionary<Object, bool>();

    [MenuItem("21tools/Sub Asset Editor")]
    public static void ShowWindow()
    {
        GetWindow<SubAssetEditorWindow>("Sub Asset Editor");
    }

    private void OnGUI()
    {
        scrollPosition = EditorGUILayout.BeginScrollView(scrollPosition);

        var newTarget = EditorGUILayout.ObjectField("Target Asset", targetAsset, typeof(Object), false);
        if (newTarget != targetAsset)
        {
            targetAsset = newTarget;
            UpdateSubAssetsList();
        }

        GUILayout.Space(20);
        GUILayout.Label("Embedding Tool", EditorStyles.boldLabel);
        DrawEmbedSection();

        GUILayout.Space(20);
        GUILayout.Label("Extracting Tool", EditorStyles.boldLabel);
        DrawExtractSection();

        EditorGUILayout.EndScrollView();
    }

    private void UpdateSubAssetsList()
    {
        subAssets.Clear();
        extractionSelections.Clear();

        if (targetAsset == null) return;

        string targetPath = AssetDatabase.GetAssetPath(targetAsset);
        if (!string.IsNullOrEmpty(targetPath))
        {
            Object[] assets = AssetDatabase.LoadAllAssetsAtPath(targetPath);
            foreach (var asset in assets)
            {
                if (asset != targetAsset && !AssetDatabase.IsMainAsset(asset))
                {
                    subAssets.Add(asset);
                    extractionSelections[asset] = false;
                }
            }
        }
    }

    private void DrawEmbedSection()
    {
        GUILayout.Label("Assets to Embed", EditorStyles.miniBoldLabel);

        var dragArea = GUILayoutUtility.GetRect(0, 50, GUILayout.ExpandWidth(true));
        GUI.Box(dragArea, "Drag Assets Here");
        HandleDragAndDrop(dragArea);

        DisplayAssetList();

        if (GUILayout.Button("Embed Assets", GUILayout.Height(35)))
        {
            if (ShouldShowEmbedConfirmation())
            {
                if (EditorUtility.DisplayDialog(
                    "Confirm Embedding",
                    "One or more of the assets you are embedding contain sub-assets. Embedding these will remove them. Are you sure you want to continue?",
                    "Embed",
                    "Cancel"))
                {
                    ProcessEmbedding();
                }
            }
            else
            {
                ProcessEmbedding();
            }
        }
    }

    private bool ShouldShowEmbedConfirmation()
    {
        foreach (var asset in assetsToEmbed)
        {
            if (asset != null && AssetDatabase.GetAssetPath(asset) != "" && AssetDatabase.LoadAllAssetsAtPath(AssetDatabase.GetAssetPath(asset)).Length > 1)
            {
                return true;
            }
        }
        return false;
    }

    private void DrawExtractSection()
    {
        GUILayout.Label("Select Sub-Assets to Extract", EditorStyles.miniBoldLabel);

        EditorGUILayout.BeginVertical("Box");
        if (subAssets.Count == 0)
        {
            GUILayout.Label("No sub-assets found", EditorStyles.centeredGreyMiniLabel);
        }
        else
        {
            foreach (var asset in subAssets)
            {
                EditorGUILayout.BeginHorizontal();

                extractionSelections[asset] = EditorGUILayout.ToggleLeft("", extractionSelections[asset], GUILayout.Width(20));
                EditorGUILayout.ObjectField(asset, typeof(Object), false, GUILayout.ExpandWidth(true));

                EditorGUILayout.EndHorizontal();
            }
        }
        EditorGUILayout.EndVertical();

        if (GUILayout.Button("Extract Selected Sub-Assets", GUILayout.Height(35)))
        {
            ProcessExtraction();
        }
    }

    private void HandleDragAndDrop(Rect dropArea)
    {
        var currentEvent = Event.current;

        if (!dropArea.Contains(currentEvent.mousePosition))
            return;

        switch (currentEvent.type)
        {
            case EventType.DragUpdated:
                DragAndDrop.visualMode = DragAndDropVisualMode.Copy;
                currentEvent.Use();
                break;

            case EventType.DragPerform:
                DragAndDrop.AcceptDrag();
                foreach (var obj in DragAndDrop.objectReferences)
                {
                    if (!assetsToEmbed.Contains(obj))
                        assetsToEmbed.Add(obj);
                }
                currentEvent.Use();
                break;
        }
    }

    private void DisplayAssetList()
    {
        EditorGUI.indentLevel++;
        for (int i = 0; i < assetsToEmbed.Count; i++)
        {
            EditorGUILayout.BeginHorizontal();

            assetsToEmbed[i] = EditorGUILayout.ObjectField(
                assetsToEmbed[i],
                typeof(Object),
                false,
                GUILayout.ExpandWidth(true)
            );

            if (GUILayout.Button("×", GUILayout.Width(20)))
            {
                assetsToEmbed.RemoveAt(i);
                EditorGUI.indentLevel--;
                GUIUtility.ExitGUI();
                return;
            }

            EditorGUILayout.EndHorizontal();
        }
        EditorGUI.indentLevel--;
    }

    private void ProcessEmbedding()
    {
        if (targetAsset == null)
        {
            Debug.LogError("Target asset is not set!");
            return;
        }

        string targetPath = AssetDatabase.GetAssetPath(targetAsset);
        if (string.IsNullOrEmpty(targetPath))
        {
            Debug.LogError("Target asset is not saved in project!");
            return;
        }

        var mainAsset = AssetDatabase.LoadMainAssetAtPath(targetPath);
        bool madeChanges = false;

        foreach (var obj in assetsToEmbed)
        {
            if (obj == null) continue;

            string assetPath = AssetDatabase.GetAssetPath(obj);
            if (AssetDatabase.IsSubAsset(obj))
            {
                Debug.LogWarning($"Skipped {obj.name} (already a sub-asset)");
                continue;
            }

            Object copy = CreateAssetCopy(obj);
            if (copy != null)
            {
                copy.name = obj.name;
                AssetDatabase.AddObjectToAsset(copy, mainAsset);
                madeChanges = true;
            }
        }

        if (madeChanges)
        {
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("Sub-assets embedded successfully!");

            // リスト更新処理を追加
            UpdateSubAssetsList();
            Repaint();
        }
    }

    private Object CreateAssetCopy(Object original)
    {
        if (original is BlendTree blendTree)
        {
            return CloneBlendTree(blendTree);
        }
        if (original is AnimatorState state)
        {
            return CloneAnimatorState(state);
        }
        if (original is AnimatorStateMachine stateMachine)
        {
            return CloneStateMachine(stateMachine);
        }
        if (original is ScriptableObject scriptableObject)
        {
            var copy = ScriptableObject.CreateInstance(scriptableObject.GetType());
            EditorUtility.CopySerialized(scriptableObject, copy);
            return copy;
        }
        if (original is Material material)
        {
            return new Material(material);
        }
        if (original is Texture2D texture)
        {
            return Instantiate(texture);
        }

        var objType = original.GetType();
        if (typeof(Object).IsAssignableFrom(objType))
        {
            var copy = Instantiate(original);
            copy.name = original.name;
            return copy;
        }

        Debug.LogWarning($"Unsupported asset type: {objType}");
        return null;
    }

    private BlendTree CloneBlendTree(BlendTree original)
    {
        var newTree = new BlendTree();
        EditorUtility.CopySerialized(original, newTree);

        var newChildren = new List<ChildMotion>();
        foreach (var child in original.children)
        {
            var newChild = new ChildMotion
            {
                motion = CloneMotion(child.motion),
                threshold = child.threshold,
                position = child.position,
                timeScale = child.timeScale,
                cycleOffset = child.cycleOffset,
                directBlendParameter = child.directBlendParameter,
                mirror = child.mirror
            };
            newChildren.Add(newChild);
        }

        newTree.children = newChildren.ToArray();
        return newTree;
    }

    private Motion CloneMotion(Motion original)
    {
        if (original == null) return null;

        if (original is BlendTree blendTree)
        {
            return CloneBlendTree(blendTree);
        }

        var copy = Instantiate(original);
        copy.name = original.name;
        return copy;
    }

    private AnimatorState CloneAnimatorState(AnimatorState original)
    {
        var newState = new AnimatorState();
        EditorUtility.CopySerialized(original, newState);
        return newState;
    }

    private AnimatorStateMachine CloneStateMachine(AnimatorStateMachine original)
    {
        var newMachine = new AnimatorStateMachine();
        EditorUtility.CopySerialized(original, newMachine);
        return newMachine;
    }

    private void ProcessExtraction()
    {
        if (targetAsset == null)
        {
            Debug.LogError("Target asset is not set!");
            return;
        }

        string targetPath = AssetDatabase.GetAssetPath(targetAsset);
        if (string.IsNullOrEmpty(targetPath))
        {
            Debug.LogError("Target asset is not saved in project!");
            return;
        }

        List<Object> selectedAssets = new List<Object>();
        foreach (var pair in extractionSelections)
        {
            if (pair.Value) selectedAssets.Add(pair.Key);
        }

        if (selectedAssets.Count == 0)
        {
            Debug.LogWarning("No sub-assets selected for extraction");
            return;
        }

        string directory = Path.GetDirectoryName(targetPath);
        int counter = 0;
        bool needsRefresh = false;

        var mainAsset = AssetDatabase.LoadMainAssetAtPath(targetPath);

        foreach (Object subAsset in selectedAssets)
        {
            if (subAsset == null) continue;

            string newPath = Path.Combine(directory, $"{targetAsset.name}_{subAsset.name}.asset");
            newPath = AssetDatabase.GenerateUniqueAssetPath(newPath);

            Object clone = CreateAssetCopy(subAsset);
            if (clone != null)
            {
                AssetDatabase.CreateAsset(clone, newPath);
                counter++;

                if (AssetDatabase.IsSubAsset(subAsset))
                {
                    AssetDatabase.RemoveObjectFromAsset(subAsset);
                    UnityEngine.Object.DestroyImmediate(subAsset, true);
                    needsRefresh = true;
                }
            }
        }

        if (counter > 0)
        {
            if (needsRefresh)
            {
                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();
            }
            Debug.Log($"Successfully extracted {counter} sub-assets to {directory}");
            UpdateSubAssetsList();
            Repaint();
        }
    }
}