using System.Collections.Generic;
using UnityEngine;

namespace LuluDungeon
{
    /// <summary>
    /// 房间模板类型（保留定义以兼容 Room.cs，新生成器不再使用）
    /// </summary>
    public enum RoomType
    {
        Straight_I, Straight_II, Corner, TJunction, CrossRoad,
        DeadEnd, Hall, Shop, Boss, Stair, Start
    }

    /// <summary>
    /// 门洞方向（保留定义以兼容 Room.cs）
    /// </summary>
    [System.Flags]
    public enum DoorDirection
    {
        None = 0, Up = 1, Right = 2, Down = 4, Left = 8
    }

    /// <summary>
    /// 房间模板（保留定义以兼容 Room.cs）
    /// </summary>
    public class RoomTemplate : MonoBehaviour
    {
        public RoomType roomType;
        public DoorDirection doors = DoorDirection.None;
        public Vector2 roomSize = new Vector2(10, 10);
    }

    /// <summary>
    /// 牢房内墙生成器 + 楼层内容生成：
    /// - 在固定框架（Floor_00）内按 5.8m 网格随机激活内墙段
    /// - 连通性保持：任何墙都不会把区域隔成不可达，牢房始终全连通
    /// - 随机确定商店（每层）/ 向导（仅0层）/ Boss（Specter）/ 野怪 / 楼梯落点
    /// - Boss 击败前楼梯封印（只记位置），ActivateStairs() 后出现可交互楼梯
    /// </summary>
    public class DungeonGenerator : MonoBehaviour
    {
        public static DungeonGenerator Instance { get; private set; }

        [Header("框架")]
        [Tooltip("牢房父节点（预设框架：地板/天花板/柱子），默认 DungeonStructure/Floor_00")]
        public Transform frameRoot;

        [Header("网格设置")]
        public int gridCount = 6;          // 6×6 格
        public float cellSize = 5.8f;      // 格边长（与现有墙段长度一致）
        public float wallHeight = 3f;      // 内墙高度
        public float wallThickness = 0.3f; // 程序墙厚度（wallPrefab 为空时用）
        [Range(0f, 1f)] public float wallDensity = 0.5f;   // 内墙激活密度

        [Header("内墙预制体（可选，缺省用程序 Cube）")]
        public GameObject wallPrefab;
        public Color wallColor = new Color(0.35f, 0.3f, 0.28f);

        [Header("内容生成")]
        public int wildMonsterMin = 3;
        public int wildMonsterMax = 6;

        private int _currentFloor = 0;
        private bool _stairsActive = false;
        private Vector3 _stairsPos;
        private readonly List<GameObject> _generated = new List<GameObject>();
        private List<Vector2Int> _reachableCells = new List<Vector2Int>();
        private readonly Vector2Int _spawnCell = new Vector2Int(2, 2);   // 出生格（中心偏下左，玩家出生地）

        // 墙位：horizontal=true 表示行 a 与 a+1 之间的横墙（跨列 b）；false 表示列 a 与 a+1 之间的纵墙（跨行 b）
        private struct WallSlot { public bool horizontal; public int a, b; }

        private static readonly Vector2Int[] Dirs = { new(0, 1), new(1, 0), new(0, -1), new(-1, 0) };

        public int CurrentFloor => _currentFloor;
        public bool StairsActive => _stairsActive;
        public List<Vector2Int> ReachableCells => _reachableCells;

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
            if (frameRoot == null)
            {
                var fg = GameObject.Find("DungeonStructure/Floor_00");
                if (fg != null) frameRoot = fg.transform;
            }
        }

        /// <summary>生成楼层：清场 → 随机内墙 → 内容落点（商店/向导/Boss/野怪/楼梯位）</summary>
        public void GenerateFloor(int floor)
        {
            CleanupFloor();
            _currentFloor = floor;
            _stairsActive = false;

            if (frameRoot == null)
            {
                Debug.LogError("[Dungeon] frameRoot missing, cannot generate floor!");
                return;
            }

            // 1. 随机内墙（连通保持）
            var walls = RollWalls();
            BuildWalls(walls);

            // 2. 可达格缓存
            _reachableCells = ComputeReachable(_spawnCell, walls);

            // 3. 内容落点
            SpawnContents();

            Debug.Log($"[Dungeon] Floor {floor} generated: {walls.Count} walls, {_generated.Count} contents.");

            // 自动存档（绑定存档槽时：进入每层即保存）
            var dm = DataManager.Instance;
            if (dm != null) dm.AutoSave();
        }

        /// <summary>Boss 击败后激活楼梯：在预定位点生成楼梯模型 + 交互</summary>
        public void ActivateStairs()
        {
            if (_stairsActive || frameRoot == null) return;
            _stairsActive = true;
            var go = BuildStairs(_stairsPos);
            _generated.Add(go);
            go.AddComponent<StairInteractable>();

            // 射线交互（与 NPC 按钮同机制）：手柄射线对准楼梯按扳机触发上楼
            var inter = go.AddComponent<UnityEngine.XR.Interaction.Toolkit.Interactables.XRSimpleInteractable>();
            inter.selectEntered.AddListener(_ =>
            {
                var si = go.GetComponent<StairInteractable>();
                if (si != null) si.OnInteract();
            });

            Debug.Log($"[Dungeon] Stairs activated on floor {_currentFloor} at {_stairsPos}");
        }

        // ==================== 内墙生成 ====================

        /// <summary>随机激活墙位并保持全区域连通（贪心 + 连通校验）</summary>
        private List<WallSlot> RollWalls()
        {
            var slots = new List<WallSlot>();
            for (int j = 0; j < gridCount - 1; j++)          // 横墙：行分隔
                for (int i = 0; i < gridCount; i++)
                    slots.Add(new WallSlot { horizontal = true, a = j, b = i });
            for (int i = 0; i < gridCount - 1; i++)          // 纵墙：列分隔
                for (int j = 0; j < gridCount; j++)
                    slots.Add(new WallSlot { horizontal = false, a = i, b = j });

            Shuffle(slots);

            var chosen = new List<WallSlot>();
            foreach (var s in slots)
            {
                // 出生区（中心 2×2 格）内部墙不激活，保证开局开阔
                if (IsSpawnAreaWall(s)) continue;
                if (Random.value > wallDensity) continue;

                chosen.Add(s);
                // 连通性保持：这面墙若导致任何格不可达则撤销
                if (!IsAllReachable(_spawnCell, chosen))
                    chosen.RemoveAt(chosen.Count - 1);
            }
            return chosen;
        }

        /// <summary>出生区（中心 2×2 格交界）的 4 条内部墙</summary>
        private bool IsSpawnAreaWall(WallSlot s)
        {
            return (s.horizontal && s.a == 2 && (s.b == 2 || s.b == 3))
                || (!s.horizontal && s.a == 2 && (s.b == 2 || s.b == 3));
        }

        /// <summary>从出生格 BFS，全部格子可达则 true</summary>
        private bool IsAllReachable(Vector2Int from, List<WallSlot> walls)
        {
            var seen = new HashSet<Vector2Int>();
            var q = new Queue<Vector2Int>();
            q.Enqueue(from);
            seen.Add(from);

            while (q.Count > 0)
            {
                var c = q.Dequeue();
                foreach (var d in Dirs)
                {
                    var n = c + d;
                    if (n.x < 0 || n.x >= gridCount || n.y < 0 || n.y >= gridCount) continue;
                    if (seen.Contains(n)) continue;
                    if (HasWall(walls, c, n)) continue;
                    seen.Add(n);
                    q.Enqueue(n);
                }
            }
            return seen.Count == gridCount * gridCount;
        }

        private bool HasWall(List<WallSlot> walls, Vector2Int from, Vector2Int to)
        {
            if (to.y == from.y + 1) return walls.Exists(w => w.horizontal && w.a == from.y && w.b == from.x);
            if (to.y == from.y - 1) return walls.Exists(w => w.horizontal && w.a == to.y && w.b == from.x);
            if (to.x == from.x + 1) return walls.Exists(w => !w.horizontal && w.a == from.x && w.b == from.y);
            if (to.x == from.x - 1) return walls.Exists(w => !w.horizontal && w.a == to.x && w.b == from.y);
            return false;
        }

        /// <summary>计算所有可达格（含出生格）</summary>
        private List<Vector2Int> ComputeReachable(Vector2Int from, List<WallSlot> walls)
        {
            var result = new List<Vector2Int>();
            var seen = new HashSet<Vector2Int>();
            var q = new Queue<Vector2Int>();
            q.Enqueue(from);
            seen.Add(from);
            while (q.Count > 0)
            {
                var c = q.Dequeue();
                result.Add(c);
                foreach (var d in Dirs)
                {
                    var n = c + d;
                    if (n.x < 0 || n.x >= gridCount || n.y < 0 || n.y >= gridCount) continue;
                    if (seen.Contains(n)) continue;
                    if (HasWall(walls, c, n)) continue;
                    seen.Add(n);
                    q.Enqueue(n);
                }
            }
            return result;
        }

        /// <summary>先建固定外墙（4 边 24 段，与预设框架齐平），再实例化/构建所有内墙段</summary>
        private void BuildWalls(List<WallSlot> walls)
        {
            BuildOuterWalls();

            foreach (var w in walls)
            {
                GameObject go;
                if (wallPrefab != null)
                {
                    go = Instantiate(wallPrefab, frameRoot);
                    go.transform.localRotation = Quaternion.Euler(0, w.horizontal ? 270f : 0f, 0);
                }
                else
                {
                    go = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    go.name = "Wall";
                    go.transform.SetParent(frameRoot, false);
                    go.GetComponent<MeshRenderer>().material = WallMaterial;
                }

                // 预制体 pivot 在墙底（y=0 贴地板）；程序 Cube pivot 在中心（y=高/2）
                float y = wallPrefab != null ? 0f : wallHeight / 2f;
                Vector3 pos;
                if (w.horizontal)
                    pos = new Vector3((w.b + 0.5f) * cellSize, y, (w.a + 1f) * cellSize);
                else
                    pos = new Vector3((w.a + 1f) * cellSize, y, (w.b + 0.5f) * cellSize);

                go.transform.localPosition = pos;
                if (wallPrefab == null)
                    go.transform.localScale = new Vector3(cellSize, wallHeight, wallThickness);

                _generated.Add(go);
            }
        }

        /// <summary>固定外墙：4 边各 gridCount 段（与手摆框架同位置同朝向）</summary>
        private void BuildOuterWalls()
        {
            float edge = gridCount * cellSize;
            for (int i = 0; i < gridCount; i++)
            {
                float along = (i + 0.5f) * cellSize;
                BuildWallPiece(new Vector3(along, 0f, edge), 270f);   // 北墙（沿 X）
                BuildWallPiece(new Vector3(along, 0f, 0f), 90f);      // 南墙（沿 X）
                BuildWallPiece(new Vector3(edge, 0f, along), 180f);   // 东墙（沿 Z）
                BuildWallPiece(new Vector3(0f, 0f, along), 0f);       // 西墙（沿 Z）
            }
        }

        /// <summary>构建单段墙（prefab 或程序 Cube），pivot 对齐地板</summary>
        private void BuildWallPiece(Vector3 pos, float rotY)
        {
            GameObject go;
            if (wallPrefab != null)
            {
                go = Instantiate(wallPrefab, frameRoot);
                go.transform.localRotation = Quaternion.Euler(0, rotY, 0);
            }
            else
            {
                go = GameObject.CreatePrimitive(PrimitiveType.Cube);
                go.name = "Wall";
                go.transform.SetParent(frameRoot, false);
                go.GetComponent<MeshRenderer>().material = WallMaterial;
                go.transform.localScale = new Vector3(cellSize, wallHeight, wallThickness);
            }
            go.transform.localPosition = pos;
            _generated.Add(go);
        }

        private Material _wallMat;
        private Material WallMaterial
        {
            get
            {
                if (_wallMat == null)
                {
                    _wallMat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                    _wallMat.color = wallColor;
                }
                return _wallMat;
            }
        }

        // ==================== 内容生成 ====================

        private void SpawnContents()
        {
            var preloader = FindFirstObjectByType<Preloader>();
            var freeCells = new List<Vector2Int>(_reachableCells);
            freeCells.RemoveAll(c => c == _spawnCell);
            if (freeCells.Count == 0) return;

            // 商店（每层 1 个）
            if (preloader != null && preloader.merchantPrefab != null)
            {
                var m = Instantiate(preloader.merchantPrefab, frameRoot);
                m.name = "NPC_Merchant";
                m.transform.localPosition = RandomCellWorld(freeCells);
                var mi = m.AddComponent<NPCIdle>();
                mi.label = "商店";
                m.AddComponent<ShopNPC>();
                _generated.Add(m);
            }

            // 向导（仅 0 层）
            if (_currentFloor == 0 && preloader != null && preloader.guidePrefab != null)
            {
                var g = Instantiate(preloader.guidePrefab, frameRoot);
                g.name = "NPC_Guide";
                g.transform.localPosition = RandomCellWorld(freeCells);
                g.transform.localRotation = Quaternion.Euler(0, 180, 0);
                var gi = g.AddComponent<NPCIdle>();
                gi.label = "向导";
                var gn = g.AddComponent<GuideNPC>();
                gn.giftSpriteData = preloader.stingRaySpriteData;
                _generated.Add(g);
            }

            // Boss（Specter，精灵持有者）
            if (preloader != null && preloader.specterPrefab != null)
            {
                var b = Instantiate(preloader.specterPrefab, frameRoot);
                b.name = "NPC_Boss";
                b.transform.localPosition = RandomCellWorld(freeCells);
                var bt = b.AddComponent<BossTrainer>();
                bt.floor = _currentFloor;
                // 锁定待机动画（演示控制器会自动过渡到 Die/Attack 导致模型倒地/沉入地板）
                var idle = b.AddComponent<NPCIdle>();
                idle.label = "";   // Boss 自带标签，不重复创建
                BossTrainer.AddTrainerCollider(b);
                _generated.Add(b);
            }
            else if (preloader != null)
            {
                Debug.LogWarning("[Dungeon] specterPrefab missing, Boss not spawned!");
            }

            // 普通野怪
            if (preloader != null)
            {
                int n = Random.Range(wildMonsterMin, wildMonsterMax + 1);
                for (int i = 0; i < n; i++)
                {
                    int idx = preloader.GetRandomMonsterIndex();
                    var pf = preloader.GetMonsterPrefab(idx);
                    if (pf == null) continue;

                    var obj = Instantiate(pf, frameRoot);
                    obj.name = "Monster_" + pf.name + "_" + i;
                    obj.transform.localPosition = RandomCellWorld(freeCells);

                    var monster = obj.AddComponent<Monster>();
                    monster.spriteData = preloader.GetMonsterSpriteData(idx);
                    monster.displayName = Spawner.GetChineseName(Spawner.CleanName(pf.name));
                    if (monster.spriteData != null)
                    {
                        monster.Init(_currentFloor);
                        Spawner.AssignRandomSkills(monster.spriteInstance);
                    }

                    obj.AddComponent<Wander>();
                    Spawner.AddMonsterCollider(obj);
                    _generated.Add(obj);
                }
            }

            // 楼梯位（封印：只记位置，Boss 击败后 ActivateStairs 生成）
            _stairsPos = RandomCellWorld(freeCells);
        }

        /// <summary>随机可达格中心 + 微偏移</summary>
        private Vector3 RandomCellWorld(List<Vector2Int> cells)
        {
            var c = cells[Random.Range(0, cells.Count)];
            float x = (c.x + 0.5f) * cellSize + Random.Range(-1.2f, 1.2f);
            float z = (c.y + 0.5f) * cellSize + Random.Range(-1.2f, 1.2f);
            return new Vector3(x, 0.05f, z);
        }

        /// <summary>程序化楼梯（三级台阶 + 根碰撞体供射线交互）</summary>
        private GameObject BuildStairs(Vector3 pos)
        {
            var root = new GameObject("Stairs");
            root.transform.SetParent(frameRoot, false);
            root.transform.localPosition = pos;

            var mat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            mat.color = new Color(0.55f, 0.42f, 0.25f);

            for (int i = 0; i < 3; i++)
            {
                var step = GameObject.CreatePrimitive(PrimitiveType.Cube);
                step.name = "Step_" + i;
                step.transform.SetParent(root.transform, false);
                step.transform.localPosition = new Vector3(i * 0.9f, 0.15f + i * 0.4f, 0f);
                step.transform.localScale = new Vector3(1.6f, 0.3f + i * 0.4f, 1.2f);
                step.GetComponent<MeshRenderer>().material = mat;
            }

            // 根碰撞体（覆盖台阶区域，供 XR 射线命中交互）
            var col = root.AddComponent<BoxCollider>();
            col.center = new Vector3(0.9f, 0.55f, 0f);
            col.size = new Vector3(2.0f, 1.2f, 1.4f);

            // 提示标签："上楼"（Billboard 朝向玩家）
            var labelGo = new GameObject("Label_上楼");
            labelGo.transform.SetParent(root.transform, false);
            labelGo.transform.localPosition = new Vector3(0.9f, 1.6f, 0f);
            var tm = labelGo.AddComponent<TextMesh>();
            tm.text = "上楼";
            tm.fontSize = 100;
            tm.characterSize = 0.02f;
            tm.color = new Color(1f, 0.9f, 0.3f);
            tm.anchor = TextAnchor.MiddleCenter;
            tm.alignment = TextAlignment.Center;
            var bb = labelGo.AddComponent<Billboard>();
            bb.flipDirection = true;
            bb.lockYAxis = true;

            return root;
        }

        // ==================== 清理 ====================

        /// <summary>清空本层所有生成内容 + 框架内遗留手摆内墙</summary>
        public void CleanupFloor()
        {
            foreach (var g in _generated)
                if (g != null) Destroy(g);
            _generated.Clear();

            if (frameRoot != null)
            {
                var stale = new List<GameObject>();
                foreach (Transform c in frameRoot)
                {
                    if (c == null) continue;
                    if (c.name.StartsWith("Wall_A") || c.name.StartsWith("Wall")) stale.Add(c.gameObject);
                }
                foreach (var o in stale) Destroy(o);
            }
        }

        private void Shuffle(List<WallSlot> arr)
        {
            for (int i = arr.Count - 1; i > 0; i--)
            {
                int j = Random.Range(0, i + 1);
                (arr[i], arr[j]) = (arr[j], arr[i]);
            }
        }
    }
}
