using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.InputSystem;

namespace RotRise.Gameplay
{
    /// <summary>
    /// GamePlay 씬의 입력과 테이블 규칙 및 화면 표시 연결
    /// </summary>
    public sealed class TableInputController : MonoBehaviour
    {
        // 씬의 카드 등장 순서와 수색 시간 설정
        [SerializeField] private InputActionAsset inputActions;
        [SerializeField] private TableBoardView view;
        [SerializeField] private LocationDefinition[] locationSequence;
        [SerializeField] private float searchDuration = 2f;

        // 입력 액션과 아직 확정되지 않은 드래그 상태
        private const string PointerAction = "UI/Point";
        private const string ClickAction = "UI/Click";
        private const string MouseBindingGroup = "Keyboard&Mouse";
        private const float DragThreshold = 4f;
        private InputAction point;
        private InputAction click;
        private Predicate<int> canInteract;
        private readonly List<TableBoard.TableCard> draggedCards = new();
        private Vector2 grabOffset;
        private bool isDragging;

        // 모델 및 상태를 읽는 Inspector용 프로퍼티
        private TableBoard board;
        public bool IsReady => board != null;
        public int CurrentDay => board?.Day ?? 0;
        public float SearchProgress => board?.SearchProgress ?? 0f;
        public int SearchSlot => board == null ? 0 : board.SearchSlot + 1;
        public int MaterialCount => board?.TableCards.Count(card => card.Material.HasValue) ?? 0;


        // Inspector에 표시할 레일 위치와 카드 식별자 조회
        public string GetLocationLabel(int slot)
        {
            // 빈 슬롯과 실제 장소 인스턴스 구분
            var location = board?.GetLocation(slot);
            return location == null ? "빈 슬롯" : $"{location.Definition.DisplayName} #{location.Id}";
        }


        // 씬 설정 검증 후 상태 및 화면 초기화
        private void Awake()
        {
            // 필수 입력 참조와 유효한 시간 확보
            if (inputActions == null || view == null || !float.IsFinite(searchDuration) || searchDuration <= 0f ||
                locationSequence == null || locationSequence.Length == 0)
            {
                FailInitialization("입력 에셋, 테이블 뷰, 장소 순서 또는 수색 시간 설정 확인 필요");
                return;
            }

            foreach (var location in locationSequence)
            {
                if (location == null || !location.HasValidRewards())
                {
                    FailInitialization($"장소 보상 데이터 확인 필요: {location?.DisplayName}");
                    return;
                }
            }

            var sourcePoint = inputActions.FindAction(PointerAction);
            var sourceClick = inputActions.FindAction(ClickAction);

            if (sourcePoint == null || sourceClick == null)
            {
                FailInitialization("UI/Point 또는 UI/Click 액션 누락");
                return;
            }

            if (!view.TryInitialize(out var layout))
            {
                FailInitialization("테이블 슬롯, 카드 배치 또는 보상 프리팹 설정 확인 필요");
                return;
            }

            // 기존 바인딩을 쓰되 씬에서 독립적으로 활성화할 복제 액션 구성
            point = sourcePoint.Clone();
            click = sourceClick.Clone();
            point.bindingMask = InputBinding.MaskByGroup(MouseBindingGroup);
            click.bindingMask = InputBinding.MaskByGroup(MouseBindingGroup);
            board = new TableBoard(locationSequence, searchDuration, layout);
            canInteract = view.CanInteractWithCard;
            view.RenderBoard(board);
        }


        // 컴포넌트 입력 활성화
        private void OnEnable()
        {
            // 초기화 실패 상태에서는 입력 없이 정지
            if (board == null)
            {
                return;
            }

            point.Enable();
            click.Enable();
            view.SetVisible(true);
        }


        // 처리 중인 드래그와 활성 입력 정리
        private void OnDisable()
        {
            // 씬 설정이 유효하지 않아 생성되지 않은 경우 제외
            if (board == null)
            {
                return;
            }

            point.Disable();
            click.Disable();
            CancelDrag();
            view.SetVisible(false);
        }


        // 씬 종료 시 복제 액션 리소스 반환
        private void OnDestroy()
        {
            point?.Dispose();
            click?.Dispose();
        }


        // 창 밖에서 버튼이 해제되더라도 드래그 상태 정리
        private void OnApplicationFocus(bool hasFocus)
        {
            if (!hasFocus && draggedCards.Count > 0)
            {
                CancelDrag();
            }
        }


        // 같은 프레임의 일차 입력을 수색 시간보다 먼저 처리
        private void Update()
        {
            // 새 보상이 생성되기 전에 진행 중인 이동만 전진
            view.AdvanceRewardScatter(Time.deltaTime);

            // 키 유지 중 재실행 없이 새 입력만 처리
            var keyboard = Keyboard.current;
            var advancesDay = keyboard != null && keyboard.spaceKey.wasPressedThisFrame;

            if (advancesDay)
            {
                draggedCards.Clear();
                isDragging = false;
                board.AdvanceDay();
                view.RenderBoard(board);
            }

            // 보상 생성 후 같은 프레임의 드래그 화면 복원
            if (board.TickSearch(Time.deltaTime, out var completedSlot))
            {
                view.PlayRewardScatter(board.TableCards, completedSlot);
                view.RenderBoard(board);
            }
            else if (board.SearchCitizenId != 0)
            {
                view.RenderProgress(board.SearchProgress);
            }

            if (!advancesDay)
            {
                UpdatePointer();
            }
        }


        // 마우스 드래그 시작과 이동 및 드롭 요청 처리
        private void UpdatePointer()
        {
            var pressed = click.WasPressedThisFrame();

            if (draggedCards.Count == 0 && !pressed)
            {
                return;
            }

            // 입력 좌표와 누른 지점의 카드 내부 상대 위치 저장
            var pointer = view.GetBoardPoint(point.ReadValue<Vector2>());

            if (pressed && draggedCards.Count == 0)
            {
                var cardId = board.FindDraggableCard(pointer, canInteract);

                if (cardId == 0)
                {
                    return;
                }

                board.GetDragCards(cardId, draggedCards);
                grabOffset = pointer - draggedCards[0].Position;
            }

            // 짧은 클릭은 배치 변경 없이 종료
            var first = draggedCards[0];
            var position = pointer - grabOffset;
            var delta = position - first.Position;
            isDragging |= delta.sqrMagnitude >= DragThreshold * DragThreshold;

            if (click.WasReleasedThisFrame())
            {
                // 실패 포함 드롭 결과를 모델의 확정 배치로 복원
                if (isDragging)
                {
                    board.TryDrop(draggedCards, pointer, position, canInteract);
                }

                CancelDrag();
                return;
            }

            if (isDragging)
            {
                var targetSlot = board.FindSearchTarget(first.Id, pointer);
                var hasStackTarget = board.TryGetStackTarget(draggedCards, pointer, position, canInteract, out _, out var targetBounds);
                view.RenderDrag(draggedCards, delta, targetSlot >= 0 ? targetSlot : board.SearchSlot, hasStackTarget ? targetBounds : null);
            }
        }


        // 일시적인 드래그를 해제하고 현재 보드 배치 표시
        private void CancelDrag()
        {
            // 모델에는 드롭 전 위치가 그대로 유지
            if (draggedCards.Count > 0)
            {
                draggedCards.Clear();
                isDragging = false;
                view.RenderBoard(board);
            }
        }


        // 씬 설정 오류를 보고하고 프레임 처리 중지
        private void FailInitialization(string reason)
        {
            Debug.LogError($"{nameof(TableInputController)}: {reason}", this);
            enabled = false;
        }
    }
}
