using System;
using ICities;
using UnityEngine;

namespace TactilePaving
{
    // 敷設した点字ブロックをセーブデータに保存・読み込みする
    public sealed class SerializableDataExtension : SerializableDataExtensionBase
    {
        private const string DataId = "TactilePaving";

        public override void OnLoadData()
        {
            try
            {
                TactilePavingData.Deserialize(serializableDataManager.LoadData(DataId));
            }
            catch (Exception e)
            {
                Debug.LogError("[TactilePaving] 点字ブロックの読み込みに失敗しました: " + e);
                TactilePavingData.Clear();
            }
        }

        public override void OnSaveData()
        {
            try
            {
                serializableDataManager.SaveData(DataId, TactilePavingData.Serialize());
            }
            catch (Exception e)
            {
                Debug.LogError("[TactilePaving] 点字ブロックの保存に失敗しました: " + e);
            }
        }
    }
}
