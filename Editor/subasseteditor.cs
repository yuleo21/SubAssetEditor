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
    
    private List<AnimatorController> referencedControllers = new List<AnimatorController>();
    private bool showReferenceFixSection = false;
    private Vector2 controllerScrollPosition;

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
        
        GUILayout.Space(20);
        DrawReferenceFixSection();

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
        List<string> pathsToDelete = new List<string>();
        Dictionary<Object, Object> originalToSubAssetMap = new Dictionary<Object, Object>();

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
                originalToSubAssetMap[obj] = copy;
                
                if (!string.IsNullOrEmpty(assetPath) && !pathsToDelete.Contains(assetPath))
                {
                    pathsToDelete.Add(assetPath);
                }
                
                madeChanges = true;
            }
        }

        if (madeChanges)
        {
            AssetDatabase.SaveAssets();
            
            if (referencedControllers.Count > 0)
            {
                UpdateAnimatorReferences(originalToSubAssetMap);
            }
            
            foreach (var path in pathsToDelete)
            {
                AssetDatabase.DeleteAsset(path);
            }
            
            AssetDatabase.Refresh();
            Debug.Log("Sub-assets embedded successfully!");
            
            assetsToEmbed.Clear();
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
    
    private void DrawReferenceFixSection()
    {
        showReferenceFixSection = EditorGUILayout.Foldout(showReferenceFixSection, "Reference Fixing Tool", true, EditorStyles.foldoutHeader);
        
        if (!showReferenceFixSection)
            return;
            
        EditorGUILayout.HelpBox("Automatically updates references in the attached AnimatorControllers. Use this when embedding or extracting animations as sub-assets.", MessageType.Info);
        
        EditorGUILayout.BeginVertical("Box");
        
        float listHeight = referencedControllers.Count == 0 
            ? 25
            : Mathf.Min(120, (referencedControllers.Count + 1) * 20 + 5);
        
        controllerScrollPosition = EditorGUILayout.BeginScrollView(
            controllerScrollPosition, 
            GUILayout.Height(listHeight)
        );
        
        for (int i = 0; i < referencedControllers.Count; i++)
        {
            EditorGUILayout.BeginHorizontal();
            
            referencedControllers[i] = (AnimatorController)EditorGUILayout.ObjectField(
                $"Controller {i+1}", 
                referencedControllers[i], 
                typeof(AnimatorController), 
                false
            );
            
            if (GUILayout.Button("×", GUILayout.Width(20)))
            {
                referencedControllers.RemoveAt(i);
                GUIUtility.ExitGUI();
                return;
            }
            
            EditorGUILayout.EndHorizontal();
        }
        
        EditorGUILayout.BeginHorizontal();
        
        AnimatorController newController = (AnimatorController)EditorGUILayout.ObjectField(
            "Add Controller", 
            null, 
            typeof(AnimatorController), 
            false
        );
        
        if (newController != null && !referencedControllers.Contains(newController))
        {
            referencedControllers.Add(newController);
        }
        
        EditorGUILayout.EndHorizontal();
        
        EditorGUILayout.EndScrollView();
        EditorGUILayout.EndVertical();
        
        EditorGUILayout.HelpBox("Warning: Finding all AnimatorControllers in a large project may cause performance issues and editor lag.", MessageType.Warning);
        
        if (GUILayout.Button("Find All AnimatorControllers in Project"))
        {
            FindAllAnimatorControllers();
        }
    }
    
    private void FindAllAnimatorControllers()
    {
        string[] guids = AssetDatabase.FindAssets("t:AnimatorController");
        referencedControllers.Clear();
        
        foreach (string guid in guids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            AnimatorController controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(path);
            if (controller != null)
            {
                referencedControllers.Add(controller);
            }
        }
        
        Debug.Log($"Found {referencedControllers.Count} animator controllers in project");
    }
    
    private void UpdateAnimatorReferences(Dictionary<Object, Object> assetMapping)
    {
        if (assetMapping == null || assetMapping.Count == 0 || referencedControllers.Count == 0)
            return;
        
        Debug.Log($"Updating references with {assetMapping.Count} mapped assets");
        int updateCount = 0;
        
        List<AnimatorController> controllersToProcess = new List<AnimatorController>(referencedControllers);
        
        foreach (AnimatorController controller in controllersToProcess)
        {
            if (controller == null)
                continue;
            
            Debug.Log($"Processing controller: {controller.name}");
            bool controllerModified = false;
            
            SerializedObject serializedController = new SerializedObject(controller);
            serializedController.Update();
            
            foreach (AnimatorControllerLayer layer in controller.layers)
            {
                if (layer.stateMachine == null)
                    continue;
                
                bool layerModified = UpdateStateMachineReferences(layer.stateMachine, assetMapping);
                if (layerModified)
                {
                    controllerModified = true;
                    Debug.Log($"Updated references in layer: {layer.name}");
                }
            }
            
            if (controllerModified)
            {
                serializedController.ApplyModifiedProperties();
                EditorUtility.SetDirty(controller);
                updateCount++;
            }
        }
        
        if (updateCount > 0)
        {
            Debug.Log($"Successfully updated references in {updateCount} animator controllers");
            AssetDatabase.SaveAssets();
        }
        else
        {
            Debug.Log("No controller references needed updating");
        }
    }
    
    private bool UpdateStateMachineReferences(AnimatorStateMachine stateMachine, Dictionary<Object, Object> assetMapping)
    {
        if (stateMachine == null)
            return false;
            
        bool wasModified = false;
        
        SerializedObject serializedStateMachine = new SerializedObject(stateMachine);
        serializedStateMachine.Update();
        
        foreach (ChildAnimatorState childState in stateMachine.states)
        {
            AnimatorState state = childState.state;
            
            if (state == null)
                continue;
                
            if (state.motion != null)
            {
                foreach (var mapping in assetMapping)
                {
                    if (state.motion == mapping.Key || 
                        (state.motion != null && mapping.Key != null && 
                         state.motion.GetInstanceID() == mapping.Key.GetInstanceID()))
                    {
                        Debug.Log($"Updating motion reference in state: {state.name}");
                        SerializedObject serializedState = new SerializedObject(state);
                        serializedState.Update();
                        
                        state.motion = mapping.Value as Motion;
                        
                        serializedState.ApplyModifiedProperties();
                        EditorUtility.SetDirty(state);
                        wasModified = true;
                        break;
                    }
                }
            }
        }
        
        foreach (ChildAnimatorState childState in stateMachine.states)
        {
            AnimatorState state = childState.state;
            
            if (state == null || !(state.motion is BlendTree))
                continue;
                
            if (UpdateBlendTreeReferences(state.motion as BlendTree, assetMapping))
            {
                EditorUtility.SetDirty(state);
                wasModified = true;
            }
        }
        
        foreach (ChildAnimatorStateMachine childStateMachine in stateMachine.stateMachines)
        {
            if (childStateMachine.stateMachine == null)
                continue;
                
            if (UpdateStateMachineReferences(childStateMachine.stateMachine, assetMapping))
            {
                wasModified = true;
            }
        }
        
        foreach (AnimatorStateTransition transition in stateMachine.anyStateTransitions)
        {
            if (transition != null && transition.destinationState != null && 
                transition.destinationState.motion != null)
            {
                foreach (var mapping in assetMapping)
                {
                    if (transition.destinationState.motion == mapping.Key ||
                        (transition.destinationState.motion != null && mapping.Key != null &&
                         transition.destinationState.motion.GetInstanceID() == mapping.Key.GetInstanceID()))
                    {
                        Debug.Log($"Updating transition destination state motion reference");
                        SerializedObject serializedTransition = new SerializedObject(transition);
                        serializedTransition.Update();
                        
                        transition.destinationState.motion = mapping.Value as Motion;
                        
                        serializedTransition.ApplyModifiedProperties();
                        EditorUtility.SetDirty(transition);
                        wasModified = true;
                        break;
                    }
                }
            }
        }
        
        if (wasModified)
        {
            serializedStateMachine.ApplyModifiedProperties();
            EditorUtility.SetDirty(stateMachine);
        }
        
        return wasModified;
    }
    
    private bool UpdateBlendTreeReferences(BlendTree blendTree, Dictionary<Object, Object> assetMapping)
    {
        if (blendTree == null)
            return false;
            
        bool wasModified = false;
        
        SerializedObject serializedBlendTree = new SerializedObject(blendTree);
        serializedBlendTree.Update();
        
        List<ChildMotion> updatedChildren = new List<ChildMotion>();
        
        for (int i = 0; i < blendTree.children.Length; i++)
        {
            ChildMotion childMotion = blendTree.children[i];
            ChildMotion updatedMotion = new ChildMotion
            {
                motion = childMotion.motion,
                threshold = childMotion.threshold,
                position = childMotion.position,
                timeScale = childMotion.timeScale,
                cycleOffset = childMotion.cycleOffset,
                directBlendParameter = childMotion.directBlendParameter,
                mirror = childMotion.mirror
            };
            
            if (childMotion.motion != null)
            {
                if (assetMapping.TryGetValue(childMotion.motion, out Object newMotion))
                {
                    Debug.Log($"Updating blend tree child motion: {childMotion.motion.name} -> {newMotion.name}");
                    updatedMotion.motion = newMotion as Motion;
                    wasModified = true;
                }
                else
                {
                    foreach (var mapping in assetMapping)
                    {
                        if (mapping.Key != null && childMotion.motion != null && 
                            mapping.Key.GetInstanceID() == childMotion.motion.GetInstanceID())
                        {
                            Debug.Log($"Updating blend tree child motion by instance ID: {childMotion.motion.name} -> {mapping.Value.name}");
                            updatedMotion.motion = mapping.Value as Motion;
                            wasModified = true;
                            break;
                        }
                    }
                }
            }
            
            updatedChildren.Add(updatedMotion);
            
            if (childMotion.motion is BlendTree childBlendTree)
            {
                if (UpdateBlendTreeReferences(childBlendTree, assetMapping))
                {
                    wasModified = true;
                }
            }
        }
        
        if (wasModified)
        {
            blendTree.children = updatedChildren.ToArray();
            
            serializedBlendTree.ApplyModifiedProperties();
            EditorUtility.SetDirty(blendTree);
            
            Debug.Log($"Updated references in blend tree: {blendTree.name}");
        }
        
        return wasModified;
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
        
        Dictionary<Object, Object> subAssetToExtractedMap = new Dictionary<Object, Object>();

        foreach (Object subAsset in selectedAssets)
        {
            if (subAsset == null) continue;

            string newPath = Path.Combine(directory, $"{subAsset.name}.asset");
            newPath = AssetDatabase.GenerateUniqueAssetPath(newPath);

            Object clone = CreateAssetCopy(subAsset);
            if (clone != null)
            {
                AssetDatabase.CreateAsset(clone, newPath);
                AssetDatabase.SaveAssets();
                
                Object createdAsset = AssetDatabase.LoadAssetAtPath(newPath, subAsset.GetType());
                
                if (createdAsset != null)
                {
                    subAssetToExtractedMap[subAsset] = createdAsset;
                    counter++;
                    Debug.Log($"Created extracted asset: {newPath}");
                }
                else
                {
                    Debug.LogError($"Failed to load created asset at: {newPath}");
                }
            }
        }
        
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        
        if (counter > 0 && referencedControllers.Count > 0)
        {
            Debug.Log($"Updating references in {referencedControllers.Count} controllers...");
            UpdateAnimatorReferences(subAssetToExtractedMap);
            
            AssetDatabase.SaveAssets();
        }
        
        foreach (Object subAsset in selectedAssets)
        {
            if (AssetDatabase.IsSubAsset(subAsset) && subAssetToExtractedMap.ContainsKey(subAsset))
            {
                AssetDatabase.RemoveObjectFromAsset(subAsset);
            }
        }

        if (counter > 0)
        {
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log($"Successfully extracted {counter} sub-assets to {directory}");
            UpdateSubAssetsList();
            Repaint();
        }
    }
}