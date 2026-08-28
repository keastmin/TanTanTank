using System;
using System.Collections.Generic;
using UnityEngine;

namespace TanTanTank
{
    internal static class HierarchyUtility
    {
        public static Transform FindDeepChild(this Transform root, string name)
        {
            if (root == null)
                return null;

            var queue = new Queue<Transform>();
            queue.Enqueue(root);
            while (queue.Count > 0)
            {
                var current = queue.Dequeue();
                if (current != root && string.Equals(current.name, name, StringComparison.Ordinal))
                    return current;

                for (var i = 0; i < current.childCount; i++)
                    queue.Enqueue(current.GetChild(i));
            }

            return null;
        }

        public static Transform FindDirectChild(this Transform root, string name)
        {
            if (root == null)
                return null;

            for (var i = 0; i < root.childCount; i++)
            {
                var child = root.GetChild(i);
                if (string.Equals(child.name, name, StringComparison.Ordinal))
                    return child;
            }

            return null;
        }

        public static T FindDeepComponent<T>(this Transform root, string name) where T : Component
        {
            return root.FindDeepChild(name)?.GetComponent<T>();
        }
    }
}
