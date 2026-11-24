using UnityEditor;
using UnityEngine;

namespace ColourWays.EditorTools
{
    /// <summary>
    /// Import settings for Art/Character:
    ///  • Character_1.fbx (the stickman exported from the Unreal project): generic rig, no imported
    ///    materials (the setup assigns ColourWays ones), no cameras/lights.
    ///  • Animations/*.fbx (Walking, Running, Happy Idle... exported from Unreal or the original
    ///    downloads): generic, reusing the stickman's avatar so the clips drive his bones, looping.
    /// </summary>
    public sealed class CharacterImport : AssetPostprocessor
    {
        internal const string Folder = "Assets/ColourWays/Art/Character/";
        internal const string AnimationFolder = Folder + "Animations/";
        internal const string ModelPath = Folder + "Character_1.fbx";

        private void OnPreprocessModel()
        {
            if (!assetPath.StartsWith(Folder)) return;
            var importer = (ModelImporter)assetImporter;
            importer.animationType = ModelImporterAnimationType.Generic;
            importer.optimizeGameObjects = false; // the procedural animator needs the bone transforms
            importer.materialImportMode = ModelImporterMaterialImportMode.None;
            importer.importCameras = false;
            importer.importLights = false;
            importer.importBlendShapes = false;
            importer.importAnimation = true;
            importer.useFileScale = true;

            if (assetPath.StartsWith(AnimationFolder))
            {
                // Bind the clips to the stickman's skeleton when it's already imported.
                Avatar avatar = null;
                foreach (var o in AssetDatabase.LoadAllAssetsAtPath(ModelPath))
                    if (o is Avatar a) { avatar = a; break; }
                if (avatar != null)
                {
                    importer.avatarSetup = ModelImporterAvatarSetup.CopyFromOther;
                    importer.sourceAvatar = avatar;
                }
                else importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
            }
            else
            {
                importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
            }
        }

        private void OnPreprocessAnimation()
        {
            if (!assetPath.StartsWith(AnimationFolder)) return;
            var importer = (ModelImporter)assetImporter;
            var clips = importer.defaultClipAnimations;
            foreach (var clip in clips)
            {
                clip.loopTime = true;
                clip.loopPose = true;
            }
            importer.clipAnimations = clips;
        }
    }
}
