using System;
using UnityEngine;

namespace InfimaGames.LowPolyShooterPack
{
    /// <summary>
    /// Marks a serialized field as requiring a value in the Inspector.
    ///
    /// The attribute is intentionally kept independent of editor-only code so
    /// components can be compiled in player builds as well.
    /// </summary>
    [AttributeUsage(AttributeTargets.Field, AllowMultiple = false)]
    public sealed class NotNullAttribute : PropertyAttribute
    {
    }
}
