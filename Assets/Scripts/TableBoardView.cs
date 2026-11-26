using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace RotRise.Gameplay
{
    /// <summary>
    /// 씬에 배치된 카드 화면과 보상 프리팹에 테이블 상태 반영
    /// </summary>
    public sealed class TableBoardView : MonoBehaviour
    {
        /// <summary>
        /// 재료 종류에 대응하는 카드 그림 설정
        /// </summary>
        [System.Serializable]
        private struct MaterialAppearance
        {
            // 재료 식별자와 제목이 비어 있는 카드 원화
            [SerializeField] private MaterialKind material;
            [SerializeField] private Texture2D art;
            internal MaterialKind Material => material;
            internal Texture2D Art => art;
        }

        /// <summary>
        /// 확정된 목적지까지 움직이는 카드의 화면 상태
        /// </summary>
        private sealed class CardMotion
        {
            // 이동 대상과 출발 위치 및 모델의 착지 좌표
            internal int CardId { get; }
            internal RectTransform Image { get; }
            internal Vector2 Origin { get; }
            internal Vector2 Destination { get; }
            internal float Duration { get; }
            internal float Elapsed { get; set; }


            // 화면 이동의 시작 위치와 확정 위치 및 시간 저장
            internal CardMotion(int cardId, RectTransform image, Vector2 origin, Vector2 destination, float duration)
            {
                // 모델의 착지 좌표와 화면 표시 대상 연결
                CardId = cardId;
                Image = image;
                Origin = origin;
                Destination = destination;
                Duration = duration;
            }
        }

        // 편집 모드에서 배치하는 테이블과 레일
        [SerializeField] private Canvas canvas;
        [SerializeField] private RectTransform table;
        [SerializeField] private RawImage[] slotFrames;
        [SerializeField] private RawImage[] locationCards;
        [SerializeField] private RawImage citizenCard;
        [SerializeField] private RectTransform cardLayer;
        [SerializeField] private Color activeSlotColor = new(0.64f, 0.87f, 0.96f, 1f);

        // 보상 카드의 시작 배치와 재사용 프리팹
        [SerializeField] private RectTransform[] rewardAnchors;
        [SerializeField] private RawImage rewardCardPrefab;
        [SerializeField] private MaterialAppearance[] materialAppearance;

        // 카드 상단 띠에 표시할 이름과 한국어 서체
        [SerializeField] private Font cardTitleFont;
        private const float CardTitleBandRatio = 0.16f;
        private const float CardTitleSideInset = 8f;
        private const int CardTitleMinSize = 11;
        private const int CardTitleMaxSize = 20;
        private static readonly Color CardTitleColor = new(1f, 0.96f, 0.88f, 1f);
        private const float PreviewOpacity = 0.68f;
        private Text[] locationTitles;

        // 수색 및 일차 표시
        [SerializeField] private RectTransform searchTrack;
        [SerializeField] private RectTransform searchFill;
        [SerializeField] private Text dayNumber;
        private const float SearchTrackGap = 14f;

        // 레이아웃 스냅샷과 테이블 카드별 실행 뷰
        private readonly Dictionary<MaterialKind, Texture2D> materialArt = new();
        private readonly Dictionary<int, RawImage> tableCardImages = new();
        private TableLayout layout;
        private Color[] frameColors;

        // 드래그 중 유효한 결합 대상의 외곽 표시
        private const float StackHighlightWidth = 4f;
        private RectTransform stackHighlight;

        // 보상 카드 확산 연출과 카드별 이동 시간
        private const float RewardTravelDuration = 0.48f;
        private const float RewardTravelInterval = 0.06f;
        private const float RewardArcHeight = 20f;
        private readonly List<CardMotion> rewardMotions = new();

        // 더미 합치기 직후 화면 안착
        private const float StackSnapDuration = 0.1f;
        private const float DropReboundDuration = 0.24f;
        private readonly List<CardMotion> stackMotions = new();


        // 씬 참조 검증과 테이블 좌표 스냅샷 생성
        internal bool TryInitialize(out TableLayout tableLayout)
        {
            tableLayout = null;

            // 고정 씬 오브젝트와 보상 프리팹 참조 확인
            if (canvas == null || table == null || citizenCard == null || cardLayer == null ||
                rewardCardPrefab == null || searchTrack == null || searchFill == null || dayNumber == null ||
                slotFrames == null || slotFrames.Length != TableLayout.SlotCount ||
                locationCards == null || locationCards.Length != TableLayout.SlotCount ||
                rewardAnchors == null || rewardAnchors.Length != TableLayout.RewardCount || materialAppearance == null ||
                cardTitleFont == null || citizenCard.texture == null)
            {
                return false;
            }

            // 재료 정의와 카드 그림을 일대일로 연결
            materialArt.Clear();

            foreach (var appearance in materialAppearance)
            {
                if (appearance.Art == null || !materialArt.TryAdd(appearance.Material, appearance.Art))
                {
                    return false;
                }
            }

            foreach (MaterialKind material in System.Enum.GetValues(typeof(MaterialKind)))
            {
                if (!materialArt.ContainsKey(material))
                {
                    return false;
                }
            }

            // Hierarchy의 슬롯 위치와 카드 위치를 같은 좌표계로 가져옴
            var bounds = new Rect[slotFrames.Length];
            var locations = new Vector2[slotFrames.Length];
            frameColors = new Color[slotFrames.Length];

            for (var slot = 0; slot < slotFrames.Length; slot++)
            {
                if (slotFrames[slot] == null || locationCards[slot] == null)
                {
                    return false;
                }

                var frame = slotFrames[slot].rectTransform;
                bounds[slot] = new Rect(GetTablePosition(frame), frame.rect.size);
                locations[slot] = GetTablePosition(locationCards[slot].rectTransform);
                frameColors[slot] = slotFrames[slot].color;
            }

            var rewards = new Vector2[rewardAnchors.Length];

            for (var index = 0; index < rewards.Length; index++)
            {
                if (rewardAnchors[index] == null)
                {
                    return false;
                }

                rewards[index] = GetTablePosition(rewardAnchors[index]);
            }

            // 화면과 드롭 판정에 동일한 편집 모드 좌표 사용
            var citizen = citizenCard.rectTransform;
            layout = new TableLayout(table.rect.size, citizen.rect.size, GetTablePosition(citizen), bounds, locations, rewards);

            // 레일과 시민 카드에 재사용할 이름 표시 생성
            locationTitles = new Text[locationCards.Length];

            for (var slot = 0; slot < locationCards.Length; slot++)
            {
                locationTitles[slot] = CreateCardTitle(locationCards[slot]);
            }

            var previewCard = locationCards[TableLayout.PreviewSlot];
            var previewColor = previewCard.color;
            previewColor.a = PreviewOpacity;
            previewCard.color = previewColor;
            CreateCardTitle(citizenCard).text = citizenCard.texture.name;

            tableLayout = layout;
            return true;
        }


        // 마우스 화면 좌표를 테이블 기준 좌표로 변환
        internal Vector2 GetBoardPoint(Vector2 screenPoint)
        {
            // 실제 캔버스 크기와 피벗을 반영한 좌상단 기준 좌표
            RectTransformUtility.ScreenPointToLocalPointInRectangle(table, screenPoint, null, out var local);
            return new Vector2(local.x - table.rect.xMin, table.rect.yMax - local.y);
        }


        // 컴포넌트 활성 상태에 화면 표시 연동
        internal void SetVisible(bool isVisible)
        {
            canvas.enabled = isVisible;
        }


        // 레일과 시민 및 재료 보상을 화면에 반영
        internal void RenderBoard(TableBoard board)
        {
            // 확정 배치 재표시 시 드래그 대상 강조 해제
            HighlightStack(null);

            // 고정 슬롯의 카드 면을 모델의 현재 장소로 갱신
            for (var slot = 0; slot < locationCards.Length; slot++)
            {
                var location = board.GetLocation(slot);
                var card = locationCards[slot];
                card.gameObject.SetActive(location != null);

                if (location != null)
                {
                    card.texture = slot == TableLayout.PreviewSlot ? location.Definition.PreviewArt : location.Definition.CardArt;
                    locationTitles[slot].text = location.Definition.DisplayName;
                }
            }

            // 카드 종류와 관계없이 모델의 더미 순서대로 화면에 배치
            var searchSlot = board.SearchSlot;

            foreach (var card in board.TableCards)
            {
                var image = GetOrCreateTableCardImage(card);
                var position = card.Id == board.SearchCitizenId
                    ? board.GetSearchCitizenPosition()
                    : card.Position;

                // 보드 재표시 중에도 이동 연출 좌표 유지
                if (!HasMotion(rewardMotions, card.Id) && !HasMotion(stackMotions, card.Id))
                {
                    SetPosition(image.rectTransform, position);
                }

                image.rectTransform.SetAsLastSibling();
            }

            // 수색 중인 시민을 슬롯 위에 표시
            if (searchSlot >= 0)
            {
                tableCardImages[board.SearchCitizenId].rectTransform.SetAsLastSibling();
            }

            HighlightSlot(searchSlot);
            dayNumber.text = board.Day.ToString();
            searchTrack.gameObject.SetActive(searchSlot >= 0);

            if (searchSlot >= 0)
            {
                var location = layout.GetLocationPosition(searchSlot);
                var offset = layout.CardSize.y + TableLayout.SearchOffset.y + SearchTrackGap;
                SetPosition(searchTrack, location + Vector2.up * offset);
                RenderProgress(board.SearchProgress);
            }
        }


        // 수색 완료 장소에서 방금 지급한 재료 카드의 이동 시작
        internal void PlayRewardScatter(IReadOnlyList<TableBoard.TableCard> cards, int completedSlot)
        {
            // 모델이 마지막에 추가한 보상 묶음과 완료한 장소 좌표 연결
            var origin = layout.GetLocationPosition(completedSlot);
            var firstReward = cards.Count - TableLayout.RewardCount;

            for (var index = firstReward; index < cards.Count; index++)
            {
                var card = cards[index];
                var image = GetOrCreateTableCardImage(card).rectTransform;
                var duration = RewardTravelDuration + RewardTravelInterval * (index - firstReward);
                SetPosition(image, origin);
                rewardMotions.Add(new CardMotion(card.Id, image, origin, card.Position, duration));
            }
        }


        // 이동 중인 보상의 클릭 및 겹치기 대상 여부 조회
        internal bool CanInteractWithCard(int cardId)
        {
            // 보이는 위치와 모델의 착지 위치가 일치한 뒤 상호작용 허용
            return !HasMotion(rewardMotions, cardId);
        }


        // 놓은 위치와 기존 더미의 화면 위치에서 확정 슬롯까지 이동 시작
        internal void PlayStackSnap(TableBoard board, int stackId,
            IReadOnlyList<TableBoard.TableCard> draggedCards, Vector2 releaseDelta, bool rebounded)
        {
            // 마지막 드래그 표시와 같은 계산으로 놓는 프레임의 좌표 적용
            foreach (var card in draggedCards)
            {
                if (card.Id != board.SearchCitizenId)
                {
                    SetPosition(tableCardImages[card.Id].rectTransform, card.Position + releaseDelta);
                }
            }

            // 합친 더미에서 위치가 달라지는 카드만 보간
            foreach (var card in board.TableCards)
            {
                if (card.StackId != stackId || card.Id == board.SearchCitizenId)
                {
                    continue;
                }

                var image = tableCardImages[card.Id].rectTransform;
                var anchored = image.anchoredPosition;
                var origin = new Vector2(anchored.x, -anchored.y);

                if (origin != card.Position)
                {
                    var duration = rebounded ? DropReboundDuration : StackSnapDuration;
                    stackMotions.Add(new CardMotion(card.Id, image, origin, card.Position, duration));
                }
            }
        }


        // 빈자리가 없거나 레일에서 거절된 카드를 원래 위치로 복귀
        internal void PlayDropReturn(TableBoard board, IReadOnlyList<TableBoard.TableCard> draggedCards, Vector2 releaseDelta)
        {
            // 릴리스 좌표에서 모델의 기존 배치까지 화면 이동 예약
            foreach (var card in draggedCards)
            {
                var image = tableCardImages[card.Id].rectTransform;
                var destination = card.Id == board.SearchCitizenId ? board.GetSearchCitizenPosition() : card.Position;
                var origin = card.Position + releaseDelta;
                SetPosition(image, origin);
                stackMotions.Add(new CardMotion(card.Id, image, origin, destination, DropReboundDuration));
            }
        }


        // 새 입력이나 비활성화 전에 더미의 화면 위치를 확정 배치에 맞춤
        internal void FinishStackSnap()
        {
            // 입력 판정 좌표와 화면 위치를 먼저 일치시킨 뒤 연출 해제
            foreach (var motion in stackMotions)
            {
                SetPosition(motion.Image, motion.Destination);
            }

            stackMotions.Clear();
        }


        // 지정한 카드에 진행 중인 화면 이동이 있는지 확인
        private static bool HasMotion(List<CardMotion> motions, int cardId)
        {
            // 카드 식별자로 이동 목록 조회
            foreach (var motion in motions)
            {
                if (motion.CardId == cardId)
                {
                    return true;
                }
            }

            return false;
        }


        // 보상은 호를 그리며 이동하고 더미는 제자리로 감속
        internal void AdvanceCardMotions(float deltaTime)
        {
            // 기존 보상 궤적과 더미의 직선 이동에 같은 감속 적용
            AdvanceMotions(rewardMotions, deltaTime, RewardArcHeight);
            AdvanceMotions(stackMotions, deltaTime, 0f);
        }


        // 화면 이동을 전진하고 완료된 카드를 확정 위치에 정렬
        private static void AdvanceMotions(List<CardMotion> motions, float deltaTime, float arcHeight)
        {
            // 이동이 끝난 항목을 역순 순회 중 제거
            for (var index = motions.Count - 1; index >= 0; index--)
            {
                var motion = motions[index];
                motion.Elapsed += deltaTime;

                if (motion.Elapsed >= motion.Duration)
                {
                    SetPosition(motion.Image, motion.Destination);
                    motions.RemoveAt(index);
                    continue;
                }

                // 착지 직전에 속도가 줄어드는 보간과 선택적 호 높이 적용
                var remaining = 1f - motion.Elapsed / motion.Duration;
                var progress = 1f - remaining * remaining;
                var lift = Mathf.Sin(progress * Mathf.PI) * arcHeight;
                var position = Vector2.Lerp(motion.Origin, motion.Destination, progress) - Vector2.up * lift;
                SetPosition(motion.Image, position);
            }
        }


        // 카드 식별자에 대응하는 화면 오브젝트 확보
        private RawImage GetOrCreateTableCardImage(TableBoard.TableCard card)
        {
            // 기존 카드 오브젝트의 위치와 이름 재사용
            if (tableCardImages.TryGetValue(card.Id, out var image))
            {
                return image;
            }

            // 첫 시민은 씬 배치 사용, 새 카드는 종류별 원본에서 복제
            if (card.Id == TableBoard.StartingCitizenId)
            {
                image = citizenCard;
            }
            else if (card.Material.HasValue)
            {
                image = Instantiate(rewardCardPrefab, cardLayer, false);
                image.texture = materialArt[card.Material.Value];
                image.name = $"{card.Material.Value} {card.Id}";
                CreateCardTitle(image).text = image.texture.name;
            }
            else
            {
                image = Instantiate(citizenCard, cardLayer, false);
                image.name = $"Citizen {card.Id}";
            }

            // 카드 선택과 드래그에 사용할 식별자 등록
            tableCardImages.Add(card.Id, image);
            return image;
        }


        // 카드 이미지와 함께 이동하는 상단 제목 생성
        private Text CreateCardTitle(RawImage card)
        {
            // 더미에서 드러나는 상단 영역에 이름 배치
            var titleObject = new GameObject("Card Title", typeof(RectTransform), typeof(Text));
            var title = titleObject.GetComponent<Text>();
            var rect = title.rectTransform;
            rect.SetParent(card.rectTransform, false);
            rect.anchorMin = new Vector2(0f, 1f - CardTitleBandRatio);
            rect.anchorMax = Vector2.one;
            rect.offsetMin = new Vector2(CardTitleSideInset, 0f);
            rect.offsetMax = new Vector2(-CardTitleSideInset, 0f);

            // 긴 이름 자동 축소와 카드 입력 통과 설정
            title.font = cardTitleFont;
            title.color = CardTitleColor;
            title.alignment = TextAnchor.MiddleCenter;
            title.supportRichText = false;
            title.raycastTarget = false;
            title.resizeTextForBestFit = true;
            title.resizeTextMinSize = CardTitleMinSize;
            title.resizeTextMaxSize = CardTitleMaxSize;
            return title;
        }


        // 확정 전 드래그 위치와 유효한 슬롯 강조 표시
        internal void RenderDrag(IReadOnlyList<TableBoard.TableCard> cards, Vector2 delta, int targetSlot, Rect? targetBounds,
            int stationaryCitizenId)
        {
            // 집은 카드와 위쪽 카드의 간격 및 그리는 순서 유지
            foreach (var card in cards)
            {
                if (card.Id == stationaryCitizenId)
                {
                    continue;
                }

                var image = tableCardImages[card.Id];
                SetPosition(image.rectTransform, card.Position + delta);
                image.rectTransform.SetAsLastSibling();
            }

            // 현재 수색 중이거나 투입 가능한 레일 위치 강조
            HighlightSlot(targetSlot);
            HighlightStack(targetBounds);
        }


        // 결합 가능한 더미의 바깥 테두리 표시
        private void HighlightStack(Rect? targetBounds)
        {
            // 드롭이나 취소로 대상이 없어진 경우 강조 숨김
            if (!targetBounds.HasValue)
            {
                if (stackHighlight != null)
                {
                    stackHighlight.gameObject.SetActive(false);
                }

                return;
            }

            // 처음 필요한 순간에 테이블 위 강조선 생성
            if (stackHighlight == null)
            {
                var highlight = new GameObject("Stack Drop Highlight", typeof(RectTransform));
                stackHighlight = (RectTransform)highlight.transform;
                stackHighlight.SetParent(table, false);
                stackHighlight.anchorMin = new Vector2(0f, 1f);
                stackHighlight.anchorMax = stackHighlight.anchorMin;
                stackHighlight.pivot = stackHighlight.anchorMin;
                AddHighlightEdge(new Vector2(0f, 1f), Vector2.one, new Vector2(0f, -StackHighlightWidth));
                AddHighlightEdge(Vector2.zero, Vector2.right, new Vector2(0f, StackHighlightWidth));
                AddHighlightEdge(Vector2.zero, Vector2.up, new Vector2(StackHighlightWidth, 0f));
                AddHighlightEdge(Vector2.right, Vector2.one, new Vector2(-StackHighlightWidth, 0f));
            }

            // 카드에 가려지지 않도록 더미 외곽 바깥에 표시
            var bounds = targetBounds.Value;
            var border = Vector2.one * StackHighlightWidth;
            SetPosition(stackHighlight, bounds.position - border);
            stackHighlight.sizeDelta = bounds.size + border * 2f;
            stackHighlight.SetAsLastSibling();
            stackHighlight.gameObject.SetActive(true);
        }


        // 레이아웃 크기에 맞춰 늘어나는 강조선 추가
        private void AddHighlightEdge(Vector2 anchorMin, Vector2 anchorMax, Vector2 inward)
        {
            // 테두리에서 안쪽으로 두께를 확보하고 입력은 카드에 전달
            var edge = new GameObject("Edge", typeof(RectTransform), typeof(Image));
            var image = edge.GetComponent<Image>();
            var rect = image.rectTransform;
            rect.SetParent(stackHighlight, false);
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.offsetMin = Vector2.Min(inward, Vector2.zero);
            rect.offsetMax = Vector2.Max(inward, Vector2.zero);
            image.color = activeSlotColor;
            image.raycastTarget = false;
        }


        // 수색 진행률을 편집된 막대 너비에 적용
        internal void RenderProgress(float fraction)
        {
            searchFill.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, searchTrack.rect.width * Mathf.Clamp01(fraction));
        }


        // 수색할 수 있는 실선 슬롯의 테두리 색 갱신
        private void HighlightSlot(int highlightedSlot)
        {
            for (var slot = 0; slot < TableLayout.PreviewSlot; slot++)
            {
                slotFrames[slot].color = slot == highlightedSlot ? activeSlotColor : frameColors[slot];
            }
        }


        // 씬에 배치된 요소의 상단 왼쪽을 테이블 좌표로 조회
        private Vector2 GetTablePosition(RectTransform element)
        {
            var corner = element.TransformPoint(new Vector3(element.rect.xMin, element.rect.yMax));
            var local = table.InverseTransformPoint(corner);
            return new Vector2(local.x - table.rect.xMin, table.rect.yMax - local.y);
        }


        // 테이블 원점에 정렬한 레이어에 카드 위치 적용
        private static void SetPosition(RectTransform element, Vector2 position)
        {
            element.anchoredPosition = new Vector2(position.x, -position.y);
        }
    }
}
