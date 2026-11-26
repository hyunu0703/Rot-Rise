using UnityEngine;

namespace RotRise.Gameplay
{
    /// <summary>
    /// 화면 표시와 카드 배치 판정에 사용하는 FHD 테이블 좌표
    /// </summary>
    internal sealed class TableLayout
    {
        // 와이어프레임 기준 해상도와 카드 규격
        internal static readonly Vector2 SearchOffset = new(40, 28);
        internal const int SlotCount = 4;
        internal const int PreviewSlot = SlotCount - 1;
        internal const int RewardCount = 3;
        private readonly Rect[] slotBounds;
        private readonly Vector2[] locationPositions;
        internal Vector2 BoardSize { get; }
        internal Vector2 CardSize { get; }
        internal Vector2 CitizenStart { get; }

        // 보상 기본 위치와 빈자리 탐색용 간격
        private readonly Vector2[] rewardPositions;
        internal const int RewardGridColumns = 9;
        internal const int RewardGridRows = 4;
        private static readonly Vector2 RewardGridStart = new(64, 120);
        private static readonly Vector2 RewardGridSpacing = new(196, 240);


        // Hierarchy의 시작 좌표와 크기를 규칙에 전달
        internal TableLayout(Vector2 boardSize, Vector2 cardSize, Vector2 citizenStart, Rect[] slotBounds,
            Vector2[] locationPositions, Vector2[] rewardPositions)
        {
            // 화면과 판정이 공유하는 시작 배치 저장
            BoardSize = boardSize;
            CardSize = cardSize;
            CitizenStart = citizenStart;
            this.slotBounds = slotBounds;
            this.locationPositions = locationPositions;
            this.rewardPositions = rewardPositions;
        }


        // 장소 카드의 기준 위치 조회
        internal Vector2 GetLocationPosition(int slot)
        {
            return locationPositions[slot];
        }


        // 테두리를 포함한 장소 슬롯의 영역 조회
        internal Rect GetSlotBounds(int slot)
        {
            return slotBounds[slot];
        }


        // 보상 카드의 우측 기본 위치 조회
        internal Vector2 GetRewardPosition(int index)
        {
            return rewardPositions[index];
        }


        // 기본 위치가 찼을 때 테이블 안의 빈자리 후보 조회
        internal static Vector2 GetRewardGridPosition(int column, int row)
        {
            return RewardGridStart + new Vector2(column * RewardGridSpacing.x, row * RewardGridSpacing.y);
        }
    }
}
