using UnityEngine;

namespace LuluDungeon
{
    /// <summary>
    /// 运行时房间行为
    /// </summary>
    public class Room : MonoBehaviour
    {
        [Header("房间信息")]
        public RoomType roomType;
        public Vector2Int gridPosition;
        [Tooltip("此房间中的野生精灵生成点")]
        public Transform[] wildSpriteSpawnPoints;

        [Header("连接房间的门")]
        public GameObject[] doorWalls; // 可开关的门/墙

        private void Start()
        {
            // 根据房间类型添加功能
            switch (roomType)
            {
                case RoomType.Stair:
                    gameObject.AddComponent<StairInteractable>();
                    break;
                case RoomType.Shop:
                    gameObject.AddComponent<ShopInteractable>();
                    break;
                case RoomType.Start:
                    // 向导NPC交互
                    break;
            }
        }

        /// <summary>
        /// 在房间中生成野生精灵
        /// </summary>
        public void SpawnWildSprites()
        {
            if (roomType == RoomType.Boss || roomType == RoomType.Shop ||
                roomType == RoomType.Stair || roomType == RoomType.Start)
                return;

            int spawnCount = roomType switch
            {
                RoomType.Hall => Random.Range(2, 5),
                RoomType.DeadEnd => Random.Range(1, 3),
                _ => Random.Range(0, 3)
            };

            for (int i = 0; i < spawnCount; i++)
            {
                if (wildSpriteSpawnPoints == null || wildSpriteSpawnPoints.Length == 0) continue;
                Transform spawnPoint = wildSpriteSpawnPoints[Random.Range(0, wildSpriteSpawnPoints.Length)];
                // 由 WildSpriteSpawner 负责实际生成
                var spawner = FindFirstObjectByType<WildSpriteSpawner>();
                if (spawner != null)
                {
                    spawner.SpawnWildSprite(spawnPoint != null ? spawnPoint : transform);
                }
            }
        }
    }

    /// <summary>
    /// 楼梯交互 - 爬层
    /// </summary>
    public class StairInteractable : MonoBehaviour, IInteractable
    {
        private bool _canInteract = true;

        public void OnInteract()
        {
            if (!_canInteract) return;
            _canInteract = false;

            var gm = GameManager.Instance;
            int nextFloor = gm.playerData.currentFloor + 1;
            gm.ChangeFloor(nextFloor);

            var dg = DungeonGenerator.Instance;
            if (dg != null)
            {
                dg.GenerateFloor(nextFloor);
            }
        }

        public string GetInteractPrompt() => "按 Grip 上楼";
    }

    /// <summary>
    /// 商店交互
    /// </summary>
    public class ShopInteractable : MonoBehaviour, IInteractable
    {
        public void OnInteract()
        {
            EventBus.Publish(EventBus.ON_SHOP_ENTER);
        }

        public string GetInteractPrompt() => "按 Grip 进入商店";
    }
}
