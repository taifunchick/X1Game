using System;
using UnityEngine;

namespace InfimaGames.LowPolyShooterPack
{
    /// <summary>
    /// Marks a serialized field whose referenced asset can be edited inline.
    ///
    /// The marker is safe to use without an editor extension; Unity still
    /// serializes and draws the referenced field normally when no custom drawer
    /// is installed.
    /// </summary>
    [AttributeUsage(AttributeTargets.Field, AllowMultiple = false)]
    public sealed class InLineEditorAttribute : PropertyAttribute
    {
    }
}
