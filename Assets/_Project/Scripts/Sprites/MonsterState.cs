namespace LuluDungeon
{
    /// <summary>
    /// 怪物状态
    /// </summary>
    public enum MonsterState
    {
        Exploring,  // 探索/非战斗：只播放移动和待机动画
        Battle      // 战斗中：启用攻击/受击/死亡动画（后续战斗系统设置）
    }
}
