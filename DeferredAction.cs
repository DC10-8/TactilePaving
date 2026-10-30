using System;
using UnityEngine;

namespace TactilePaving
{
    // UIのイベント処理中にそのUI自身を作り直すと危ないので、次のフレームで実行する
    internal sealed class DeferredAction : MonoBehaviour
    {
        private Action action;

        public static void Run(Action action)
        {
            GameObject gameObject = new GameObject("TactilePavingDeferredAction");
            gameObject.AddComponent<DeferredAction>().action = action;
        }

        private void Update()
        {
            try
            {
                action();
            }
            catch (Exception e)
            {
                Debug.LogError("[TactilePaving] " + e);
            }

            Destroy(gameObject);
        }
    }
}
