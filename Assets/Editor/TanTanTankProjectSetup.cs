using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Fusion;
using Fusion.Editor;
using TanTanTank;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

internal static class TanTanTankProjectSetup
{
    private const string ResourceRoot = "Assets/Resources/TanTanTank";
    private const string BalancePath = ResourceRoot + "/Game Balance.asset";
    private const string PalettePath = ResourceRoot + "/Tank Color Palette.asset";
    private const int WallLayer = 7;
    private const int PreviewLayer = 8;

    [InitializeOnLoadMethod]
    private static void ScheduleFirstSetup()
    {
        if (!File.Exists(BalancePath))
            EditorApplication.delayCall += ConfigureProject;
    }

    [MenuItem("TanTanTank/Configure Project")]
    public static void ConfigureProject()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            return;

        try
        {
            EnsureFolders();
            EnsureLayers();
            var balance = EnsureBalance();
            var palette = EnsurePalette();
            var lineMaterial = EnsureLineMaterial();
            var previewTextures = EnsurePreviewTextures();

            ConfigureTankPrefab(lineMaterial);
            ConfigureProjectilePrefab();
            ConfigureWorldPrefab("Assets/Prefabs/World/World 1.prefab");
            ConfigureWorldPrefab("Assets/Prefabs/World/World 2.prefab");
            ConfigureUiPrefabs(previewTextures);
            CreateNetworkOnlyPrefabs();
            CopyRuntimePrefabs();
            ConfigureScenes(previewTextures);

            var finalBalance = AssetDatabase.LoadAssetAtPath<GameBalanceConfig>(BalancePath);
            var finalPalette = AssetDatabase.LoadAssetAtPath<TankColorPalette>(PalettePath);
            if (finalBalance != null)
                EditorUtility.SetDirty(finalBalance);
            if (finalPalette != null)
                EditorUtility.SetDirty(finalPalette);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            NetworkProjectConfigUtilities.RebuildPrefabTable();
            AssetDatabase.SaveAssets();
            Debug.Log("TanTanTank project configuration completed.");
        }
        catch (Exception exception)
        {
            Debug.LogException(exception);
        }
    }

    private static void EnsureFolders()
    {
        EnsureFolder("Assets", "Resources");
        EnsureFolder("Assets/Resources", "TanTanTank");
        EnsureFolder(ResourceRoot, "Network");
        EnsureFolder(ResourceRoot, "UI");
    }

    private static void EnsureFolder(string parent, string child)
    {
        var path = parent + "/" + child;
        if (!AssetDatabase.IsValidFolder(path))
            AssetDatabase.CreateFolder(parent, child);
    }

    private static void EnsureLayers()
    {
        var tagManager = new SerializedObject(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset")[0]);
        var layers = tagManager.FindProperty("layers");
        layers.GetArrayElementAtIndex(WallLayer).stringValue = "Wall";
        layers.GetArrayElementAtIndex(PreviewLayer).stringValue = "Preview";
        tagManager.ApplyModifiedProperties();
    }

    private static GameBalanceConfig EnsureBalance()
    {
        var asset = AssetDatabase.LoadAssetAtPath<GameBalanceConfig>(BalancePath);
        if (asset != null)
            return asset;
        asset = ScriptableObject.CreateInstance<GameBalanceConfig>();
        AssetDatabase.CreateAsset(asset, BalancePath);
        return asset;
    }

    private static TankColorPalette EnsurePalette()
    {
        var asset = AssetDatabase.LoadAssetAtPath<TankColorPalette>(PalettePath);
        if (asset == null)
        {
            asset = ScriptableObject.CreateInstance<TankColorPalette>();
            AssetDatabase.CreateAsset(asset, PalettePath);
        }

        var materials = AssetDatabase.FindAssets("t:Material", new[] { "Assets/Materials/Tank/Body" })
            .Select(AssetDatabase.GUIDToAssetPath)
            .Select(AssetDatabase.LoadAssetAtPath<Material>)
            .Where(material => material != null)
            .ToList();
        var preferredOrder = new[]
        {
            "Tank_White", "Tank_Red", "Tank_Yellow", "Tank_Green",
            "Tank_Skyblue", "Tank_Blue", "Tank_Purple", "Tank_Pink"
        };
        asset.colors = preferredOrder
            .Select(name => materials.FirstOrDefault(material => material.name == name))
            .Where(material => material != null)
            .ToArray();
        if (asset.colors.Length != preferredOrder.Length)
            asset.colors = materials.OrderBy(material => material.name).ToArray();
        EditorUtility.SetDirty(asset);
        return asset;
    }

    private static Material[] MatchMaterialsToColorButtons(List<Material> materials)
    {
        var result = new List<Material>();
        var root = PrefabUtility.LoadPrefabContents("Assets/Prefabs/UI/Main Canvas.prefab");
        try
        {
            var colorRoot = FindDeep(root.transform, "Color Select Buttons");
            if (colorRoot == null)
                return Array.Empty<Material>();
            var unused = new List<Material>(materials);
            for (var i = 0; i < colorRoot.childCount; i++)
            {
                var image = colorRoot.GetChild(i).GetComponent<Image>();
                if (image == null || unused.Count == 0)
                    continue;
                var target = image.color;
                var closest = unused.OrderBy(material => ColorDistance(target, MaterialColor(material))).First();
                result.Add(closest);
                unused.Remove(closest);
            }
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
        return result.ToArray();
    }

    private static Color MaterialColor(Material material)
    {
        if (material.HasProperty("_BaseColor"))
            return material.GetColor("_BaseColor");
        if (material.HasProperty("_Color"))
            return material.GetColor("_Color");
        return Color.white;
    }

    private static float ColorDistance(Color left, Color right)
    {
        var red = left.r - right.r;
        var green = left.g - right.g;
        var blue = left.b - right.b;
        return red * red + green * green + blue * blue;
    }

    private static Material EnsureLineMaterial()
    {
        const string path = ResourceRoot + "/Aim Preview.mat";
        var material = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (material != null)
            return material;
        material = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
        material.color = new Color(1f, 1f, 1f, 0.75f);
        AssetDatabase.CreateAsset(material, path);
        return material;
    }

    private static RenderTexture[] EnsurePreviewTextures()
    {
        return new[] { EnsureRenderTexture(0), EnsureRenderTexture(1) };
    }

    private static RenderTexture EnsureRenderTexture(int slot)
    {
        var path = $"{ResourceRoot}/UI/Player {slot + 1} Preview.renderTexture";
        var texture = AssetDatabase.LoadAssetAtPath<RenderTexture>(path);
        if (texture != null)
            return texture;
        texture = new RenderTexture(512, 512, 24, RenderTextureFormat.ARGB32)
        {
            name = $"Player {slot + 1} Preview",
            antiAliasing = 2
        };
        AssetDatabase.CreateAsset(texture, path);
        return texture;
    }

    private static void ConfigureTankPrefab(Material lineMaterial)
    {
        EditPrefab("Assets/Prefabs/Player/Tank.prefab", root =>
        {
            GetOrAdd<NetworkObject>(root);
            RemoveIfPresent<NetworkTransform>(root);
            RemoveIfPresent<Rigidbody>(root);
            var movement = GetOrAdd<NetworkCharacterController>(root);
            var balance = Resources.Load<GameBalanceConfig>("TanTanTank/Game Balance");
            if (balance != null)
            {
                movement.maxSpeed = balance.tankMoveSpeed;
                movement.rotationSpeed = balance.tankTurnSpeed * Mathf.Deg2Rad;
            }
            var characterController = root.GetComponent<CharacterController>();
            characterController.center = new Vector3(0f, 1.05f, 0f);
            characterController.height = 2.06f;
            characterController.radius = 1f;
            characterController.skinWidth = 0.08f;
            var physicsCollider = FindDeep(root.transform, "Physics Collider");
            if (physicsCollider != null)
                RemoveIfPresent<BoxCollider>(physicsCollider.gameObject);
            var appearance = GetOrAdd<TankAppearance>(root);
            var tankController = GetOrAdd<TankNetworkController>(root);
            tankController.ConfigureReferences(
                FindDeep(root.transform, "Turret"),
                FindDeep(root.transform, "Fire Transform"),
                appearance);

            var line = GetOrAdd<LineRenderer>(root);
            line.useWorldSpace = true;
            line.startWidth = 0.3f;
            line.endWidth = 0.3f;
            line.positionCount = 0;
            line.sharedMaterial = lineMaterial;
            line.textureMode = LineTextureMode.Stretch;
            GetOrAdd<AimPrediction>(root);

            var hurtbox = FindDeep(root.transform, "Hurtbox");
            if (hurtbox != null)
            {
                hurtbox.gameObject.layer = LayerMask.NameToLayer("Hurtbox");
                var collider = hurtbox.GetComponent<Collider>();
                if (collider != null)
                    collider.isTrigger = true;
                GetOrAdd<TankHurtbox>(hurtbox.gameObject);
            }

            var cooldown = FindDeep(root.transform, "Fire Cooldown UI");
            if (cooldown != null)
                GetOrAdd<TankWorldSpaceCooldownUI>(cooldown.gameObject);
        });
    }

    private static void ConfigureProjectilePrefab()
    {
        EditPrefab("Assets/Prefabs/Player/Projectile.prefab", root =>
        {
            GetOrAdd<NetworkObject>(root);
            GetOrAdd<NetworkTransform>(root);
            GetOrAdd<NetworkProjectile>(root);
            var sphere = GetOrAdd<SphereCollider>(root);
            sphere.isTrigger = true;
            root.layer = LayerMask.NameToLayer("Ignore Raycast");
        });
    }

    private static void ConfigureWorldPrefab(string path)
    {
        EditPrefab(path, root =>
        {
            GetOrAdd<NetworkObject>(root);
            SetWallLayer(FindDeep(root.transform, "Outside Walls"));
            SetWallLayer(FindDeep(root.transform, "Outside Wall"));
            SetWallLayer(FindDeep(root.transform, "Inside Walls"));
        });
    }

    private static void SetWallLayer(Transform root)
    {
        if (root == null)
            return;
        foreach (var transform in root.GetComponentsInChildren<Transform>(true))
            transform.gameObject.layer = WallLayer;
    }

    private static void ConfigureUiPrefabs(RenderTexture[] previewTextures)
    {
        EditPrefab("Assets/Prefabs/UI/Main Canvas.prefab", root =>
        {
            GetOrAdd<MainMenuUIController>(root);
            var colorRoot = FindDeep(root.transform, "Color Select Buttons");
            if (colorRoot != null)
            {
                for (var i = 0; i < colorRoot.childCount; i++)
                {
                    var button = colorRoot.GetChild(i).GetComponent<Button>();
                    if (button == null)
                        continue;
                    GetOrAdd<LobbyColorButton>(button.gameObject).Configure(i);
                }
            }
            var sections = FindAllDeep(root.transform, "Player Tank View Section");
            for (var i = 0; i < sections.Count && i < previewTextures.Length; i++)
                EnsurePreviewImage(sections[i], previewTextures[i]);
        });
        EditPrefab("Assets/Prefabs/UI/Game Canvas.prefab", root => GetOrAdd<GameHUDController>(root));
        EditPrefab("Assets/Prefabs/UI/Notice Window.prefab", root => GetOrAdd<NoticeWindowController>(root));
    }

    private static void EnsurePreviewImage(Transform section, RenderTexture texture)
    {
        var child = section.Find("Tank Preview");
        if (child == null)
        {
            var go = new GameObject("Tank Preview", typeof(RectTransform), typeof(CanvasRenderer), typeof(RawImage));
            child = go.transform;
            child.SetParent(section, false);
            var rect = (RectTransform)child;
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }
        var image = child.GetComponent<RawImage>();
        image.texture = texture;
        image.color = Color.white;
        image.raycastTarget = false;
    }

    private static void CreateNetworkOnlyPrefabs()
    {
        CreateSimplePrefab(ResourceRoot + "/Network/Session Player State.prefab", typeof(SessionPlayerState));
        CreateSimplePrefab(ResourceRoot + "/Network/Match Runtime.prefab", typeof(MatchController));
    }

    private static void CreateSimplePrefab(string path, Type behaviourType)
    {
        if (AssetDatabase.LoadAssetAtPath<GameObject>(path) != null)
            AssetDatabase.DeleteAsset(path);
        var go = new GameObject(Path.GetFileNameWithoutExtension(path));
        go.AddComponent<NetworkObject>();
        go.AddComponent(behaviourType);
        PrefabUtility.SaveAsPrefabAsset(go, path);
        UnityEngine.Object.DestroyImmediate(go);
    }

    private static void CopyRuntimePrefabs()
    {
        CopyAsset("Assets/Prefabs/Player/Tank.prefab", ResourceRoot + "/Network/Tank.prefab");
        CopyAsset("Assets/Prefabs/Player/Projectile.prefab", ResourceRoot + "/Network/Projectile.prefab");
        CopyAsset("Assets/Prefabs/World/World 1.prefab", ResourceRoot + "/Network/World 1.prefab");
        CopyAsset("Assets/Prefabs/World/World 2.prefab", ResourceRoot + "/Network/World 2.prefab");
        CopyAsset("Assets/Prefabs/UI/Notice Window.prefab", ResourceRoot + "/UI/Notice Window.prefab");
    }

    private static void CopyAsset(string source, string destination)
    {
        if (AssetDatabase.LoadMainAssetAtPath(destination) != null)
            AssetDatabase.DeleteAsset(destination);
        if (!AssetDatabase.CopyAsset(source, destination))
            throw new InvalidOperationException($"Could not copy {source} to {destination}");
    }

    private static void ConfigureScenes(RenderTexture[] previewTextures)
    {
        var activeScenePath = SceneManager.GetActiveScene().path;
        ConfigureMainScene(previewTextures);
        ConfigureGameScene();
        if (!string.IsNullOrEmpty(activeScenePath) && File.Exists(activeScenePath))
            EditorSceneManager.OpenScene(activeScenePath, OpenSceneMode.Single);
    }

    private static void ConfigureMainScene(RenderTexture[] previewTextures)
    {
        const string scenePath = "Assets/Scenes/MainScene.unity";
        var scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);
        var mainCamera = FindSceneObject("Main Camera")?.GetComponent<Camera>();
        if (mainCamera != null)
        {
            mainCamera.tag = "MainCamera";
            mainCamera.cullingMask &= ~(1 << PreviewLayer);
        }

        var renderRoot = FindSceneObject("Tank View Render")?.transform;
        var previewCamera = FindSceneObject("Tank View Render Camera")?.GetComponent<Camera>();
        var rotator = FindSceneObject("Tank Rotator")?.transform;
        if (renderRoot != null && previewCamera != null && rotator != null)
        {
            PreparePreviewTank(rotator, 0);
            ConfigurePreviewCamera(previewCamera, previewTextures[0]);
            previewCamera.transform.SetParent(renderRoot, true);
            rotator.SetParent(renderRoot, true);

            var oldSecondCamera = FindSceneObject("Player2 Preview Camera");
            if (oldSecondCamera != null)
                UnityEngine.Object.DestroyImmediate(oldSecondCamera);
            var oldSecondRotator = FindSceneObject("Player2 Tank Rotator");
            if (oldSecondRotator != null)
                UnityEngine.Object.DestroyImmediate(oldSecondRotator);

            var secondCameraObject = UnityEngine.Object.Instantiate(previewCamera.gameObject, renderRoot);
            secondCameraObject.name = "Player2 Preview Camera";
            secondCameraObject.transform.position += Vector3.right * 30f;
            var secondCamera = secondCameraObject.GetComponent<Camera>();
            ConfigurePreviewCamera(secondCamera, previewTextures[1]);

            var secondRotatorObject = UnityEngine.Object.Instantiate(rotator.gameObject, renderRoot);
            secondRotatorObject.name = "Player2 Tank Rotator";
            secondRotatorObject.transform.position += Vector3.right * 30f;
            PreparePreviewTank(secondRotatorObject.transform, 1);
        }

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
    }

    private static void PreparePreviewTank(Transform rotator, int slot)
    {
        var tank = FindDeep(rotator, "Tank");
        if (tank == null)
            return;

        var outermost = PrefabUtility.GetOutermostPrefabInstanceRoot(tank.gameObject);
        if (outermost != null)
            PrefabUtility.UnpackPrefabInstance(outermost, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);

        foreach (var collider in tank.GetComponentsInChildren<Collider>(true))
            collider.enabled = false;
        RemoveIfPresent<AimPrediction>(tank.gameObject);
        RemoveIfPresent<TankNetworkController>(tank.gameObject);
        RemoveIfPresent<NetworkTransform>(tank.gameObject);
        RemoveIfPresent<NetworkObject>(tank.gameObject);
        RemoveIfPresent<Rigidbody>(tank.gameObject);
        var cooldown = tank.GetComponentInChildren<TankWorldSpaceCooldownUI>(true);
        if (cooldown != null)
            cooldown.gameObject.SetActive(false);
        var line = tank.GetComponent<LineRenderer>();
        if (line != null)
            line.enabled = false;
        foreach (var transform in tank.GetComponentsInChildren<Transform>(true))
            transform.gameObject.layer = PreviewLayer;

        var preview = GetOrAdd<TankPreviewVisual>(rotator.gameObject);
        preview.Configure(slot, tank.GetComponent<TankAppearance>());
    }

    private static void ConfigurePreviewCamera(Camera camera, RenderTexture texture)
    {
        camera.cullingMask = 1 << PreviewLayer;
        camera.targetTexture = texture;
        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = new Color(0f, 0f, 0f, 0f);
        var listener = camera.GetComponent<AudioListener>();
        if (listener != null)
            UnityEngine.Object.DestroyImmediate(listener);
    }

    private static void ConfigureGameScene()
    {
        const string scenePath = "Assets/Scenes/GameScene.unity";
        var scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);
        var mainCamera = FindSceneObject("Main Camera")?.GetComponent<Camera>();
        if (mainCamera != null)
            mainCamera.tag = "MainCamera";
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
    }

    private static GameObject FindSceneObject(string name)
    {
        foreach (var root in SceneManager.GetActiveScene().GetRootGameObjects())
        {
            if (root.name == name)
                return root;
            var child = FindDeep(root.transform, name);
            if (child != null)
                return child.gameObject;
        }
        return null;
    }

    private static void EditPrefab(string path, Action<GameObject> edit)
    {
        var root = PrefabUtility.LoadPrefabContents(path);
        try
        {
            edit(root);
            PrefabUtility.SaveAsPrefabAsset(root, path);
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }

    private static T GetOrAdd<T>(GameObject gameObject) where T : Component
    {
        var existing = gameObject.GetComponent<T>();
        return existing != null ? existing : gameObject.AddComponent<T>();
    }

    private static Component GetOrAdd(GameObject gameObject, Type type)
    {
        var existing = gameObject.GetComponent(type);
        return existing != null ? existing : gameObject.AddComponent(type);
    }

    private static void RemoveIfPresent<T>(GameObject gameObject) where T : Component
    {
        var component = gameObject.GetComponent<T>();
        if (component != null)
            UnityEngine.Object.DestroyImmediate(component);
    }

    private static Transform FindDeep(Transform root, string name)
    {
        if (root == null)
            return null;
        var queue = new Queue<Transform>();
        queue.Enqueue(root);
        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            if (current != root && current.name == name)
                return current;
            for (var i = 0; i < current.childCount; i++)
                queue.Enqueue(current.GetChild(i));
        }
        return null;
    }

    private static List<Transform> FindAllDeep(Transform root, string name)
    {
        return root.GetComponentsInChildren<Transform>(true).Where(child => child.name == name).ToList();
    }
}
