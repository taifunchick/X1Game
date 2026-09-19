//Copyright 2022, Infima Games. All Rights Reserved.

using System;
using UnityEngine;

namespace InfimaGames.LowPolyShooterPack
{
    /// <summary>
    /// FeelState. Contains information on different things that happen in a single state.
    /// </summary>
    [Serializable]
    public struct FeelState
    {
        #region PROPERTIES

        /// <summary>
        /// Offset.
        /// </summary>
        public FeelStateOffset Offset => offset;
        /// <summary>
        /// SwayData.
        /// </summary>
        public SwayData SwayData => swayData;

        /// <summary>
        /// JumpingCurves.
        /// </summary>
        public ACurves JumpingCurves => jumpingCurves;
        /// <summary>
        /// FallingCurves.
        /// </summary>
        public ACurves FallingCurves => fallingCurves;
        /// <summary>
        /// LandingCurves.
        /// </summary>
        public ACurves LandingCurves => landingCurves;
        
        #endregion
        
        #region FIELDS SERIALIZED
        
        
        [Tooltip("Offset.")]
        [SerializeField, InLineEditor]
        public FeelStateOffset offset;
        
        
        [Tooltip("Settings relating to sway.")]
        [SerializeField, InLineEditor]
        public SwayData swayData;
        

        [Tooltip("Animation curves played when the character jumps.")]
        [SerializeField, InLineEditor]
        public ACurves jumpingCurves;
        
        
        [Tooltip("Animation curves played when the character falls.")]
        [SerializeField, InLineEditor]
        public ACurves fallingCurves;
        

        [Tooltip("Animation curves played when the character lands.")]
        [SerializeField, InLineEditor]
        public ACurves landingCurves;
        
        #endregion
    }
}