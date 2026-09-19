//Copyright 2022, Infima Games. All Rights Reserved.

using UnityEngine;

namespace InfimaGames.LowPolyShooterPack.Interface
{
    /// <summary>
    /// Player Interface.
    /// </summary>
    public class CanvasSpawner : MonoBehaviour
    {
        #region FIELDS SERIALIZED

        
        [Tooltip("Canvas prefab spawned at start. Displays the player's user interface.")]
        [SerializeField]
        private GameObject canvasPrefab;
        
        [Tooltip("Quality settings menu prefab spawned at start. Used for switching between different quality settings in-game.")]
        [SerializeField]
        private GameObject qualitySettingsPrefab;

        #endregion

        #region UNITY

        bool spawned;

        /// <summary>
        /// Spawn HUD only when this component is enabled (local player).
        /// Awake still runs on disabled behaviours, so spawning there would duplicate UI for remotes.
        /// </summary>
        private void OnEnable()
        {
            if (spawned)
                return;

            spawned = true;
            if (canvasPrefab != null)
                Instantiate(canvasPrefab);
            if (qualitySettingsPrefab != null)
                Instantiate(qualitySettingsPrefab);
        }

        #endregion
    }
}