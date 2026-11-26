using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace RotRise.Gameplay
{
    /// <summary>
    /// 카드 배치와 일차 레일 및 수색 결과의 실행 상태 소유
    /// </summary>
    internal sealed class TableBoard
    {
        /// <summary>
        /// 테이블 카드 한 장의 식별자와 확정 배치 및 더미 소속
        /// </summary>
        internal readonly struct TableCard
        {
            // 개별 카드의 종류와 테이블 좌표
            internal int Id { get; }
            internal MaterialKind? Material { get; }
            internal Vector2 Position { get; }
            internal int StackId { get; }


            // 새 카드 또는 배치 변경 스냅샷 생성
            internal TableCard(int id, MaterialKind? material, Vector2 position, int stackId = 0)
            {
                // 카드 식별자와 좌표 보존
                Id = id;
                Material = material;
                Position = position;
                StackId = stackId == 0 ? id : stackId;
            }
        }

        /// <summary>
        /// 레일에서 이동해도 유지되는 장소 카드의 식별자와 수색 진행 시간
        /// </summary>
        internal sealed class LocationCard
        {
            // 장소 인스턴스와 공유 설정 참조 및 누적 수색 시간
            internal int Id { get; }
            internal LocationDefinition Definition { get; }
            internal float SearchElapsed { get; set; }


            // 공유 설정을 참조하는 새 장소 카드 생성
            internal LocationCard(int id, LocationDefinition definition)
            {
                // 인스턴스 식별자와 정의 연결
                Id = id;
                Definition = definition;
            }
        }

        /// <summary>
        /// 현재 수색에 투입된 시민과 대상 장소 연결
        /// </summary>
        private sealed class Search
        {
            // 슬롯과 독립적인 투입 시민과 작업 대상
            internal int CitizenId { get; }
            internal LocationCard Location { get; }


            // 수색 시작 시 두 카드의 식별 범위 고정
            internal Search(int citizenId, LocationCard location)
            {
                // 투입 시민과 대상 장소의 참조 저장
                CitizenId = citizenId;
                Location = location;
            }
        }

        // 테이블 카드의 뒤에서 앞으로 그리는 순서와 일차 레일 원본
        private readonly List<TableCard> tableCards = new();
        private readonly LocationCard[] rail = new LocationCard[TableLayout.SlotCount];
        private readonly LocationDefinition[] locationSequence;
        private readonly TableLayout layout;
        internal const int StartingCitizenId = 1;
        private int nextCardId = StartingCitizenId;
        private int nextLocationIndex;
        internal int Day { get; private set; } = 1;
        internal IReadOnlyList<TableCard> TableCards { get; }

        // 카드 더미의 세로 간격과 결합에 필요한 면적 비율
        private const float StackStepRatio = 0.16f;
        private const float MinimumOverlapAreaRatio = 0.12f;
        private const float MinimumOverlapAxisRatio = 0.14f;
        private const float CenterReachRatio = 0.62f;
        private Vector2 StackStep => Vector2.up * (layout.CardSize.y * StackStepRatio);

        // 장소 주변의 보상 착지 후보 탐색 범위
        private const int RewardScatterAttempts = 48;
        private const float RewardMinimumDistanceRatio = 0.8f;
        private const float RewardMaximumDistanceRatio = 2.2f;

        // 진행 중 수색과 표시용 읽기 결과
        private readonly float searchDuration;
        private Search search;
        internal int SearchCitizenId => search?.CitizenId ?? 0;
        internal int SearchSlot => search == null ? -1 : Array.IndexOf(rail, search.Location);
        internal float SearchProgress => search == null ? 0f : search.Location.SearchElapsed / searchDuration;


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
        internal int FindDraggableCard(Vector2 pointer, Predicate<int> canInteract)
        {
            // 레일 위에 그려지는 수색 시민을 복귀 위치보다 먼저 확인
            var searchCitizenId = SearchCitizenId;

            if (searchCitizenId != 0 && new Rect(GetSearchCitizenPosition(), layout.CardSize).Contains(pointer) &&
                canInteract(searchCitizenId))
            {
                return searchCitizenId;
            }

            // 화면에서 앞에 그려지는 카드부터 선택
            for (var index = tableCards.Count - 1; index >= 0; index--)
            {
                var card = tableCards[index];

                if (card.Id != searchCitizenId && new Rect(card.Position, layout.CardSize).Contains(pointer) && canInteract(card.Id))
                {
                    return card.Id;
                }
            }

            return 0;
        }


        // 레일에 표시되는 수색 시민의 좌표 조회
        internal Vector2 GetSearchCitizenPosition()
        {
            // 화면 표시와 드래그 시작점에 동일한 슬롯 오프셋 사용
            return layout.GetLocationPosition(SearchSlot) + TableLayout.SearchOffset;
        }


        // 집은 카드부터 같은 더미의 위쪽 카드까지 드래그 목록에 복사
        internal void GetDragCards(int cardId, List<TableCard> cards)
        {
            // 확정 배치 순서를 임시 드래그에도 사용
            cards.Clear();
            var firstIndex = FindTableIndex(cardId);
            var first = tableCards[firstIndex];

            // 수색 시민을 잡으면 테이블의 복귀 위치 대신 레일에서 드래그 시작
            if (cardId == SearchCitizenId)
            {
                cards.Add(new TableCard(first.Id, first.Material, GetSearchCitizenPosition(), first.StackId));
                return;
            }

            for (var index = firstIndex; index < tableCards.Count; index++)
            {
                var card = tableCards[index];

                if (card.StackId == first.StackId)
                {
                    cards.Add(card);
                }
            }
        }


        // 대상 슬롯의 수색 가능 여부 조회
        internal int FindSearchTarget(IReadOnlyList<TableCard> draggedCards, Vector2 pointer)
        {
            // 더미에 소속돼 있어도 실제로 한 장만 집은 시민이면 수색 허용
            var first = draggedCards[0];

            if (draggedCards.Count != 1 || first.Material.HasValue || (search != null && first.Id != search.CitizenId))
            {
                return -1;
            }

            // 슬롯 안에 놓인 공개 장소만 수색 대상으로 인정
            var slot = FindSlot(pointer);
            return slot >= 0 && slot < TableLayout.PreviewSlot && rail[slot] != null ? slot : -1;
        }


        // 드롭을 확정하고 합친 경우 최종 더미 식별자 전달
        internal bool TryDrop(IReadOnlyList<TableCard> draggedCards, Vector2 pointer, Vector2 position,
            Predicate<int> canInteract, out int mergedStackId)
        {
            // 드래그 시작 카드의 확정 위치와 더미 소속 조회
            mergedStackId = 0;
            var first = draggedCards[0];
            var firstIndex = FindTableIndex(first.Id);
            var targetSlot = FindSearchTarget(draggedCards, pointer);

            if (targetSlot >= 0)
            {
                search = new Search(first.Id, rail[targetSlot]);
                return true;
            }

            // 레일 위에서는 일반 카드 배치 금지
            if (FindSlot(pointer) >= 0)
            {
                return false;
            }

            // 결합 대상의 바닥 위치를 기준으로 재료와 시민 순서 재배치
            var hasTarget = TryGetStackTarget(draggedCards, pointer, position, canInteract, out var targetStackId, out var targetBounds);
            var cardsToPlace = draggedCards;

            if (hasTarget)
            {
                position = targetBounds.position;
                cardsToPlace = MergeStackCards(targetStackId, firstIndex, draggedCards);
            }
            else if (!CanPlaceStack(new Rect(position, layout.CardSize + StackStep * (draggedCards.Count - 1)), firstIndex, 0))
            {
                return false;
            }

            // 수색 시민을 유효한 테이블 위치에 놓으면 장소에 진행 시간 보존
            if (first.Id == SearchCitizenId)
            {
                search = null;
            }

            // 배치 확정 후 원본에서 이동분과 대상 더미를 제거
            for (var index = tableCards.Count - 1; index >= 0; index--)
            {
                var stackId = tableCards[index].StackId;

                if (stackId == targetStackId || (index >= firstIndex && stackId == first.StackId))
                {
                    tableCards.RemoveAt(index);
                }
            }

            // 새 바닥 카드의 식별자로 더미 소속과 그리기 순서 확정
            var newStackId = cardsToPlace[0].Id;

            for (var index = 0; index < cardsToPlace.Count; index++)
            {
                var card = cardsToPlace[index];
                tableCards.Add(new TableCard(card.Id, card.Material, position + StackStep * index, newStackId));
            }

            mergedStackId = hasTarget ? newStackId : 0;
            return true;
        }


        // 대상 더미와 드래그 카드의 새 순서 계산
        private List<TableCard> MergeStackCards(int targetStackId, int firstIndex, IReadOnlyList<TableCard> draggedCards)
        {
            // 같은 더미에 다시 놓을 때 이동 카드를 제외한 대상 카드 수집
            var cards = new List<TableCard>();
            var sourceStackId = tableCards[firstIndex].StackId;

            for (var index = 0; index < tableCards.Count; index++)
            {
                var card = tableCards[index];

                if (card.StackId == targetStackId && (targetStackId != sourceStackId || index < firstIndex))
                {
                    cards.Add(card);
                }
            }

            // 같은 종류 안에서는 대상 다음에 이동 카드가 오도록 안정 정렬
            cards.AddRange(draggedCards);
            return cards.OrderBy(card => !card.Material.HasValue).ToList();
        }


        // 드롭과 강조에 공통으로 사용할 유효한 결합 대상 조회
        internal bool TryGetStackTarget(IReadOnlyList<TableCard> draggedCards, Vector2 pointer, Vector2 position,
            Predicate<int> canInteract, out int targetStackId, out Rect targetBounds)
        {
            // 레일 위에서는 수색 상호작용만 허용
            targetStackId = 0;
            targetBounds = default;

            if (FindSlot(pointer) >= 0)
            {
                return false;
            }

            // 카드 단위의 접촉 면적과 중심 거리 비교 준비
            var first = draggedCards[0];
            var firstIndex = FindTableIndex(first.Id);
            var cardSize = layout.CardSize;
            var draggedBounds = new Rect(position, cardSize);
            var bestDistance = float.PositiveInfinity;

            for (var index = tableCards.Count - 1; index >= 0; index--)
            {
                var card = tableCards[index];

                if ((index >= firstIndex && card.StackId == first.StackId) || card.Id == SearchCitizenId)
                {
                    continue;
                }

                // 한쪽 폭이나 면적이 작은 모서리 접촉 제외
                var bounds = new Rect(card.Position, cardSize);
                var width = Mathf.Min(bounds.xMax, draggedBounds.xMax) - Mathf.Max(bounds.xMin, draggedBounds.xMin);
                var height = Mathf.Min(bounds.yMax, draggedBounds.yMax) - Mathf.Max(bounds.yMin, draggedBounds.yMin);

                if (width < cardSize.x * MinimumOverlapAxisRatio || height < cardSize.y * MinimumOverlapAxisRatio ||
                    width * height < cardSize.x * cardSize.y * MinimumOverlapAreaRatio)
                {
                    continue;
                }

                // 직접 가리키는 카드 우선, 그 외에는 가까운 중심부터 선택
                var isPointedAt = bounds.Contains(pointer);
                var delta = draggedBounds.center - bounds.center;
                var offset = new Vector2(delta.x / cardSize.x, delta.y / cardSize.y);
                var distance = offset.sqrMagnitude;

                if ((!isPointedAt && (Mathf.Abs(offset.x) > CenterReachRatio || Mathf.Abs(offset.y) > CenterReachRatio ||
                    distance >= bestDistance)) || !canInteract(card.Id))
                {
                    continue;
                }

                // 실제로 합친 영역이 들어가는 더미만 강조 대상으로 전달
                var candidateBounds = GetTargetBounds(card.StackId, firstIndex);
                var mergedBounds = new Rect(candidateBounds.position, candidateBounds.size + StackStep * draggedCards.Count);

                if (!CanPlaceStack(mergedBounds, firstIndex, card.StackId))
                {
                    continue;
                }

                targetStackId = card.StackId;
                targetBounds = candidateBounds;
                bestDistance = distance;

                if (isPointedAt)
                {
                    return true;
                }
            }

            return targetStackId != 0;
        }


        // 드래그 중인 카드를 뺀 대상 더미의 표시 영역 조회
        private Rect GetTargetBounds(int stackId, int firstMovingIndex)
        {
            // 확정 배치 순서에 연속된 같은 더미의 카드 수 확인
            var baseIndex = FindTableIndex(stackId);
            var count = 1;

            for (var index = baseIndex + 1; index < tableCards.Count && tableCards[index].StackId == stackId; index++)
            {
                if (index == firstMovingIndex)
                {
                    break;
                }

                count++;
            }

            return new Rect(tableCards[baseIndex].Position, layout.CardSize + StackStep * (count - 1));
        }


        // 드롭 결과가 테이블과 다른 더미의 배치를 침범하는지 확인
        private bool CanPlaceStack(Rect bounds, int firstMovingIndex, int targetStackId)
        {
            // 레일과 테이블 경계 검사
            if (!IsTableArea(bounds))
            {
                return false;
            }

            // 움직이는 카드와 결합 대상의 기존 영역 제외
            var sourceStackId = tableCards[firstMovingIndex].StackId;

            for (var index = 0; index < tableCards.Count; index++)
            {
                var card = tableCards[index];
                var isMoving = index >= firstMovingIndex && card.StackId == sourceStackId;

                if (!isMoving && card.StackId != targetStackId && bounds.Overlaps(new Rect(card.Position, layout.CardSize)))
                {
                    return false;
                }
            }

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


        // 경과 시간 반영 후 완료 시점의 장소 슬롯 전달
        internal bool TickSearch(float deltaTime, out int completedSlot)
        {
            // 활성 수색만 전진
            completedSlot = -1;

            if (search == null)
            {
                return false;
            }

            search.Location.SearchElapsed += deltaTime;

            if (search.Location.SearchElapsed < searchDuration)
            {
                return false;
            }

            // 현재 슬롯의 장소를 한 번 소모하고 수색 원본 해제
            completedSlot = SearchSlot;
            var location = search.Location.Definition;
            rail[completedSlot] = null;
            search = null;

            // 세 위치를 먼저 정하고 독립 추첨 결과를 별도 카드로 지급
            var positions = PlanRewardPositions(layout.GetLocationPosition(completedSlot));

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


        // 카드 배치 전체가 테이블의 레일 바깥에 들어오는지 확인
        private bool IsTableArea(Rect bounds)
        {
            // 테이블 경계와 예약된 레일 영역 확인
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

            return true;
        }


        // 보상용 빈자리와 이번 보상 묶음의 예약 위치 확인
        private bool IsPlacementFree(Vector2 position, IReadOnlyList<Vector2> planned)
        {
            // 테이블과 레일 경계 규칙 공유
            var bounds = new Rect(position, layout.CardSize);

            if (!IsTableArea(bounds))
            {
                return false;
            }

            // 수색에 투입된 시민의 복귀 위치도 사용 중인 자리로 취급
            foreach (var card in tableCards)
            {
                if (bounds.Overlaps(new Rect(card.Position, layout.CardSize)))
                {
                    return false;
                }
            }

            foreach (var reserved in planned)
            {
                if (bounds.Overlaps(new Rect(reserved, layout.CardSize)))
                {
                    return false;
                }
            }

            return true;
        }


        // 보상 한 묶음에 사용할 겹치지 않는 좌표 선택
        private List<Vector2> PlanRewardPositions(Vector2 origin)
        {
            // 앞서 정한 카드의 착지 위치를 다음 후보 판정에 반영
            var planned = new List<Vector2>(TableLayout.RewardCount);

            for (var index = 0; index < TableLayout.RewardCount; index++)
            {
                // 주변과 테이블에 빈자리가 부족할 때도 남은 보상 지급
                if (!TryFindRewardPosition(origin, planned, out var position))
                {
                    position = layout.GetRewardPosition(index);
                }

                planned.Add(position);
            }

            return planned;
        }


        // 장소 주변의 무작위 빈자리 또는 가장 가까운 격자 후보 선택
        private bool TryFindRewardPosition(Vector2 origin, IReadOnlyList<Vector2> planned, out Vector2 position)
        {
            // 카드 규격에 비례하는 범위에서 방향과 거리를 독립 추첨
            var minDistance = layout.CardSize.y * RewardMinimumDistanceRatio;
            var maxDistance = layout.CardSize.y * RewardMaximumDistanceRatio;

            for (var attempt = 0; attempt < RewardScatterAttempts; attempt++)
            {
                var angle = UnityEngine.Random.Range(0f, 2f * Mathf.PI);
                var direction = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
                var candidate = origin + direction * UnityEngine.Random.Range(minDistance, maxDistance);

                if (IsPlacementFree(candidate, planned))
                {
                    position = candidate;
                    return true;
                }
            }

            // 가까운 후보가 막힌 경우에만 더 넓은 테이블에서 빈자리 탐색
            position = default;
            var bestDistance = float.PositiveInfinity;

            for (var row = 0; row < TableLayout.RewardGridRows; row++)
            {
                for (var column = 0; column < TableLayout.RewardGridColumns; column++)
                {
                    var candidate = TableLayout.GetRewardGridPosition(column, row);
                    var distance = (candidate - origin).sqrMagnitude;

                    if (distance < bestDistance && IsPlacementFree(candidate, planned))
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
