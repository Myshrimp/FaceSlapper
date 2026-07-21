using System.Collections.Generic;
using UnityEngine;

namespace FaceSlapper.Battle
{
    /// <summary>
    /// 出生点：挂在场景中的空物体上即自动注册到静态列表，
    /// RoomComponent 生成玩家时按序号轮询使用。
    /// </summary>
    public class PlayerSpawnPoints : MonoBehaviour
    {
        public static readonly List<Transform> Points = new List<Transform>(8);

        private void OnEnable() => Points.Add(transform);

        private void OnDisable() => Points.Remove(transform);
    }
}
