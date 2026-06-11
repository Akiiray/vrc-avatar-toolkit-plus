using System;

namespace Akiiray.VRCAvatarToolkitPlus.Editor
{
    /// <summary>
    /// VRC Avatar Toolkit Plus 共通定数。
    /// Editor拡張のメニュー名、外部型名などを一箇所に集約します。
    /// </summary>
    public static class ToolkitConstants
    {
        public const string ProductName = "VRC Avatar Toolkit Plus";
        public const string MenuRoot = "Tools/" + ProductName;
        public const string GameObjectMenuRoot = "GameObject/" + ProductName;

        public const string AvatarDescriptorFullName = "VRC.SDK3.Avatars.Components.VRCAvatarDescriptor";
        public const string ExpressionParametersFullName = "VRC.SDK3.Avatars.ScriptableObjects.VRCExpressionParameters";

        public const string DateTimeFormat = "yyyy-MM-dd HH:mm:ss";
    }
}
