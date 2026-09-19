//Copyright 2022, Infima Games. All Rights Reserved.

using UnityEngine;

namespace InfimaGames.LowPolyShooterPack
{
    /// <summary>
    /// This class contains utility functions to help with manipulation of AnimationCurve values.
    /// </summary>
    public static class AnimationCurveUtilities
    {
        /// <summary>
        /// Evaluates a set of curves (in Vector3 format: x,y,z) at a specific time and returns the value as a Vector3.
        /// </summary>
        public static Vector3 EvaluateCurves(this AnimationCurve[] animationCurves, float time)
        {
            //Make sure that the AnimationCurve values are valid.
            if (animationCurves == null || animationCurves.Length != 3)
                return default;
            
            //Return.
            return new Vector3
            {
                //X.
                x = Evaluate(animationCurves[0], time),
                //Y.
                y = Evaluate(animationCurves[1], time),
                //Z.
                z = Evaluate(animationCurves[2], time)
            };
        }

        /// <summary>
        /// Evaluates a single curve, treating a missing curve as a flat zero value.
        /// </summary>
        private static float Evaluate(AnimationCurve animationCurve, float time)
            => animationCurve != null ? animationCurve.Evaluate(time) : 0.0f;
    }
}