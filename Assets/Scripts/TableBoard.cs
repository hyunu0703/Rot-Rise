using System;
using System.Collections.Generic;
using UnityEngine;

namespace RotRise.Gameplay
{
    /// <summary>
    /// 카드 배치와 일차 레일 및 수색 결과의 실행 상태 소유
    /// </summary>
    internal sealed class TableBoard
    {
        /// <summary>
        /// 테이블 카드 한 장의 식별자와 마지막 배치 위치
        /// </summary>
        internal readonly struct TableCard
        {
            // 개별 카드의 종류와 테이블 좌표
            internal int Id { get; }
            internal MaterialKind? Material { get; }
            internal Vector2 Position { get; }


            // 새 카드 또는 배치 변경 스냅샷 생성
            internal TableCard(int id, MaterialKind? material, Vector2 position)
            {
                // 카드 식별자와 좌표 보존
                Id = id;
                Material = material;
                Position = position;
            }
        }

        /// <summary>
        /// 레일에서 이동해도 유지되는 장소 카드 한 장의 식별자
        /// </summary>
        internal sealed class LocationCard
        {
            // 장소 인스턴스와 공유 설정 참조
            internal int Id { get; }
            internal LocationDefinition Definition { get; }


            // 공유 설정을 참조하는 새 장소 카드 생성
            internal LocationCard(int id, LocationDefinition definition)
            {
                // 인스턴스 식별자와 정의 연결
                Id = id;
                Definition = definition;
            }
        }

        /// <summary>
        /// 투입 시민과 장소 카드에 묶인 수색 타이머
        /// </summary>
        private sealed class Search
        {
            // 슬롯과 독립적인 작업 대상과 경과 시간
            internal int CitizenId { get; }
            internal LocationCard Location { get; }
            internal float Elapsed { get; set; }


            // 수색 시작 시 두 카드의 식별 범위 고정
            internal Search(int citizenId, LocationCard location)
            {
                // 투입 시민과 대상 장소의 참조 저장
                CitizenId = citizenId;
                Location = location;
            }
        }

        // 테이블 카드와 일차 레일의 원본 상태
        private readonly List<TableCard> tableCards = new();
        private readonly LocationCard[] rail = new LocationCard[TableLayout.SlotCount];
        private readonly LocationDefinition[] locationSequence;
        private readonly TableLayout layout;
        private int nextCardId = 1;
        private int nextLocationIndex;
        internal int Day { get; private set; } = 1;
        internal IReadOnlyList<TableCard> TableCards { get; }

        // 진행 중 수색과 표시용 읽기 결과
        private readonly float searchDuration;
        private Search search;
        internal int SearchCitizenId => search?.CitizenId ?? 0;
        internal int SearchSlot => search == null ? -1 : Array.IndexOf(rail, search.Location);
        internal float SearchProgress => search == null ? 0f : search.Elapsed / searchDuration;


        // 첫날 레일과 시민 카드 준비
        internal TableBoard(LocationDefinition[] locationSequence, float searchDuration, TableLayout layout)
        {
            // 검증한 씬 설정과 읽기 전용 카드 목록 연결
            this.locationSequence = locationSequence;
            this.searchDuration = searchDuration;
            this.layout = layout;
            TableCards = tableCards.AsReadOnly();

            tableCards.Add(new TableCard(nextCardId++, null, layout.CitizenStart));

            for (var slot = 0; slot < rail.Length; slot++)
            {
                rail[slot] = CreateNextLocation();
            }
        }


        // 현재 슬롯의 장소 카드 조회
        internal LocationCard GetLocation(int slot)
        {
            return rail[slot];
        }


        // 포인터 아래에 있는 드래그 가능한 테이블 카드 조회
        internal int FindDraggableCard(Vector2 pointer)
        {
            // 마지막에 생성한 카드를 먼저 선택해 포화 상태의 카드 접근 유지
            for (var index = tableCards.Count - 1; index >= 0; index--)
            {
                var card = tableCards[index];

                if (card.Id != SearchCitizenId && new Rect(card.Position, layout.CardSize).Contains(pointer))
                {
                    return card.Id;
                }
            }

            return 0;
        }


        // 드래그 시작 위치를 얻기 위한 현재 카드 배치 조회
        internal Vector2 GetCardPosition(int cardId)
        {
            return tableCards[FindTableIndex(cardId)].Position;
        }


        // 대상 슬롯의 수색 가능 여부 조회
        internal int FindSearchTarget(int cardId, Vector2 pointer)
        {
            // 작업 중인 수색과 시민 외 카드 거절
            var index = FindTableIndex(cardId);

            if (search != null || index < 0 || tableCards[index].Material.HasValue)
            {
                return -1;
            }

            // 현재 공개 장소에만 수색 대상 연결
            var slot = FindSlot(pointer);
            return slot >= 0 && slot < TableLayout.PreviewSlot && rail[slot] != null ? slot : -1;
        }


        // 카드 드롭 요청을 수색 시작 또는 테이블 이동으로 확정
        internal bool TryDrop(int cardId, Vector2 pointer, Vector2 position)
        {
            // 없는 카드와 수색 중인 시민 이동 거절
            var index = FindTableIndex(cardId);

            if (index < 0 || cardId == SearchCitizenId)
            {
                return false;
            }

            // 슬롯 위 드롭은 장소 상호작용으로만 처리
            var targetSlot = FindSearchTarget(cardId, pointer);

            if (targetSlot >= 0)
            {
                search = new Search(cardId, rail[targetSlot]);
                return true;
            }

            // 허용되지 않은 슬롯과 테이블 충돌은 기존 위치 유지
            if (FindSlot(pointer) >= 0 || !IsPlacementFree(position, cardId))
            {
                return false;
            }

            var card = tableCards[index];
            tableCards[index] = new TableCard(card.Id, card.Material, position);
            return true;
        }


        // 일차를 올리고 기존 장소와 빈 슬롯을 왼쪽으로 이동
        internal void AdvanceDay()
        {
            // 제거되는 장소의 수색만 보상 없이 해제
            if (search != null && search.Location == rail[0])
            {
                search = null;
            }

            // 남은 장소 인스턴스와 빈 슬롯을 그대로 전달
            Array.Copy(rail, 1, rail, 0, TableLayout.PreviewSlot);
            rail[TableLayout.PreviewSlot] = CreateNextLocation();
            Day++;
        }


        // 경과 시간 반영 후 수색 완료 상태 변경 결과 전달
        internal bool TickSearch(float deltaTime)
        {
            // 활성 수색만 전진
            if (search == null)
            {
                return false;
            }

            search.Elapsed += deltaTime;

            if (search.Elapsed < searchDuration)
            {
                return false;
            }

            // 현재 슬롯의 장소를 한 번 소모하고 수색 원본 해제
            var slot = SearchSlot;
            var location = search.Location.Definition;
            rail[slot] = null;
            search = null;

            // 세 위치를 먼저 정하고 독립 추첨 결과를 별도 카드로 지급
            var positions = PlanRewardPositions();

            foreach (var position in positions)
            {
                tableCards.Add(new TableCard(nextCardId++, location.DrawMaterial(), position));
            }

            return true;
        }


        // 설정된 순서의 다음 장소 카드 생성
        private LocationCard CreateNextLocation()
        {
            // 장소 종류는 순환하고 카드 식별자는 새로 발급
            var definition = locationSequence[nextLocationIndex];
            nextLocationIndex = (nextLocationIndex + 1) % locationSequence.Length;
            return new LocationCard(nextCardId++, definition);
        }


        // 포인터를 포함하는 레일 슬롯 조회
        private int FindSlot(Vector2 pointer)
        {
            for (var slot = 0; slot < TableLayout.SlotCount; slot++)
            {
                // 카드가 소모된 슬롯도 배치 영역으로 유지
                if (layout.GetSlotBounds(slot).Contains(pointer))
                {
                    return slot;
                }
            }

            return -1;
        }


        // 식별자에 해당하는 테이블 카드 인덱스 조회
        private int FindTableIndex(int cardId)
        {
            for (var index = 0; index < tableCards.Count; index++)
            {
                // 배치 순서와 카드 식별자의 독립 유지
                if (tableCards[index].Id == cardId)
                {
                    return index;
                }
            }

            return -1;
        }


        // 테이블 경계와 다른 카드의 배치를 고려한 빈자리 판정
        private bool IsPlacementFree(Vector2 position, int ignoredCardId, IReadOnlyList<Vector2> planned = null)
        {
            // 카드 전체가 테이블 안에 있어야 이동 가능
            var bounds = new Rect(position, layout.CardSize);
            var board = layout.BoardSize;

            if (bounds.xMin < 0f || bounds.yMin < 0f || bounds.xMax > board.x || bounds.yMax > board.y)
            {
                return false;
            }

            for (var slot = 0; slot < TableLayout.SlotCount; slot++)
            {
                if (bounds.Overlaps(layout.GetSlotBounds(slot)))
                {
                    return false;
                }
            }

            // 수색에 투입된 시민의 복귀 위치도 사용 중인 자리로 취급
            foreach (var card in tableCards)
            {
                if (card.Id != ignoredCardId && bounds.Overlaps(new Rect(card.Position, layout.CardSize)))
                {
                    return false;
                }
            }

            if (planned != null)
            {
                foreach (var reserved in planned)
                {
                    if (bounds.Overlaps(new Rect(reserved, layout.CardSize)))
                    {
                        return false;
                    }
                }
            }

            return true;
        }


        // 보상 한 묶음에 사용할 겹치지 않는 좌표 선택
        private Vector2[] PlanRewardPositions()
        {
            var positions = new Vector2[TableLayout.RewardCount];
            var planned = new List<Vector2>(positions.Length);

            for (var index = 0; index < positions.Length; index++)
            {
                // 기본 위치를 우선하고 차 있으면 가까운 빈 후보 탐색
                var preferred = layout.GetRewardPosition(index);

                if (!TryFindRewardPosition(preferred, planned, out positions[index]))
                {
                    // 여유 공간 부족 시 세 장을 우측 기본 위치에 올려 지급 유지
                    for (var fallback = 0; fallback < positions.Length; fallback++)
                    {
                        positions[fallback] = layout.GetRewardPosition(fallback);
                    }

                    return positions;
                }

                planned.Add(positions[index]);
            }

            return positions;
        }


        // 보상 기본 좌표와 가장 가까운 빈 후보 판정
        private bool TryFindRewardPosition(Vector2 preferred, IReadOnlyList<Vector2> planned, out Vector2 position)
        {
            position = preferred;

            if (IsPlacementFree(preferred, 0, planned))
            {
                return true;
            }

            // 희소한 보상 생성 시점에만 대체 위치 탐색
            var bestDistance = float.PositiveInfinity;

            for (var row = 0; row < TableLayout.RewardGridRows; row++)
            {
                for (var column = 0; column < TableLayout.RewardGridColumns; column++)
                {
                    var candidate = TableLayout.GetRewardGridPosition(column, row);
                    var distance = (candidate - preferred).sqrMagnitude;

                    if (distance < bestDistance && IsPlacementFree(candidate, 0, planned))
                    {
                        bestDistance = distance;
                        position = candidate;
                    }
                }
            }

            return !float.IsPositiveInfinity(bestDistance);
        }
    }
}
