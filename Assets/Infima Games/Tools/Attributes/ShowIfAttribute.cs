using System;
using UnityEngine;

namespace InfimaGames.LowPolyShooterPack
{
    /// <summary>
    /// Marks a serialized field that should be shown when a boolean condition
    /// on the containing object has the expected value.
    /// </summary>
    [AttributeUsage(AttributeTargets.Field, AllowMultiple = false)]
    public sealed class ShowIfAttribute : PropertyAttribute
    {
        /// <summary>
        /// Name of the boolean field or property used as the condition.
        /// </summary>
        public string ConditionName { get; }

        /// <summary>
        /// Value that makes the decorated field visible.
        /// </summary>
        public bool ExpectedValue { get; }

        public ShowIfAttribute(string conditionName, bool expectedValue)
        {
            ConditionName = conditionName;
            ExpectedValue = expectedValue;
        }
    }
}
