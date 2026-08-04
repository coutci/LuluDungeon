using UnityEngine;

namespace LuluDungeon
{
    /// <summary>
    /// 场景初始化器 - 挂载到场景中的空对象上
    /// 负责设置GameManager、DungeonGenerator等核心系统
    /// </summary>
    public class SceneInitializer : MonoBehaviour
    {
        [Header("核心预制体")]
        public GameObject gameManagerPrefab;
        public GameObject dataManagerPrefab;
        public GameObject battleManagerPrefab;

        [Header("自动初始化")]
        public bool initializeOnStart = true;

        private void Start()
        {
            if (initializeOnStart)
            {
                InitializeCoreSystems();
                Invoke(nameof(StartGame), 0.5f);
            }
        }

        private void InitializeCoreSystems()
        {
            // 确保核心单例存在
            if (GameManager.Instance == null && gameManagerPrefab != null)
                Instantiate(gameManagerPrefab);

            if (DataManager.Instance == null && dataManagerPrefab != null)
                Instantiate(dataManagerPrefab);

            if (BattleManager.Instance == null && battleManagerPrefab != null)
                Instantiate(battleManagerPrefab);
        }

        private void StartGame()
        {
            var gm = GameManager.Instance;
            var dg = DungeonGenerator.Instance;

            if (gm == null || dg == null)
            {
                Debug.LogError("[SceneInitializer] Core systems missing!");
                return;
            }

            if (gm.activeSaveSlot >= 1)
            {
                // 由主菜单进入：数据已就绪（开始新游戏或已读档），直接生成当前层
                dg.GenerateFloor(gm.playerData.currentFloor);
                if (gm.playerData.bossDefeated) dg.ActivateStairs();
                // 地牢 BGM
                if (AudioManager.Instance != null) AudioManager.Instance.PlayBgm(BgmType.Dungeon);
                Debug.Log($"[SceneInitializer] Game started on floor {gm.playerData.currentFloor} (slot {gm.activeSaveSlot})");                if (gm.playerData.bossDefeated) dg.ActivateStairs();
                Debug.Log($"[SceneInitializer] Game started on floor {gm.playerData.currentFloor} (slot {gm.activeSaveSlot})");
                return;
            }

            // 编辑器直接打开地牢场景（调试）：有存档就读槽 1，无档从 0 层新游戏
            var dm = DataManager.Instance;
            if (dm != null && dm.HasSaveFile(1) && dm.LoadGame(1))
            {
                gm.activeSaveSlot = 1;
                // 读档成功：生成当前层；楼梯状态由 bossDefeated 恢复
                dg.GenerateFloor(gm.playerData.currentFloor);
                if (gm.playerData.bossDefeated) dg.ActivateStairs();
            }
            else
            {
                // 新游戏（或无有效存档） - 从第 0 层开始
                gm.playerData.currentFloor = 0;
                dg.GenerateFloor(0);
                // 初始精灵由 0 层向导对话赠送（避免重复发放）
            }

            Debug.Log($"[SceneInitializer] Game started on floor {gm.playerData.currentFloor}");
        }

        // 初始精灵赠送已移除：由 0 层向导对话统一发放（避免重复获得）
    }
}
