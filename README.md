# Racing

Unity 6 (6000.3.11f1, URP) 3D 서킷 레이싱 게임 프로토타입.

## 게임 내용
- 약 1.85 km 도심 시가지 서킷 (직각 코너 10개, 콘크리트 방호벽·광고판, 보도, 가로등, 빌딩 숲, 출발 게이트·관중석)
- 메인 화면: 배경에서 차들이 자동 주행하고 시네마틱 카메라가 중계 (START RACE / SETTINGS / CONTROLS / QUIT, 마우스 지원)
- 설정: 랩 수(1~10), 시간대(낮/밤), AI 난이도(EASY/NORMAL/HARD) — 저장됨
- 기록: 베스트 랩, 최고 순위 저장 및 메인 화면 표시
- 플레이어 1명 + AI 5명
- 아케이드 레이캐스트 서스펜션 차량 물리 (핸드브레이크 드리프트, 잔디 감속)
- 순위/랩/랩타임/베스트랩 HUD, 미니맵, 역주행 경고, 카운트다운, 결과 화면
- 낮/밤 테마: 밤에는 빌딩 창문 불빛, 가로등·차량 헤드라이트 실제 조명, 야간 하늘과 안개
- 합성 엔진음 (5단 기어 피치)

## 조작
| 동작 | 키보드 | 게임패드 |
|---|---|---|
| 가속 / 브레이크·후진 | W·↑ / S·↓ | RT / LT |
| 조향 | A D · ← → | 왼쪽 스틱 |
| 핸드브레이크 | Space | A |
| 차량 리셋 | R | Y |
| 카메라 전환 | C | RB |
| 일시정지 | ESC | Select |
| 메뉴: 선택 / 확인 / 뒤로 | ↑ ↓ / Enter / ESC (마우스 클릭) | 방향패드 / A / B |
| 설정 값 변경 | ← → | 방향패드 ← → |
| 메뉴: 낮·밤 바로 전환 | T | |
| 일시정지 중: 재시작 / 메인 화면 | R / Q | Y / B |

## 열기 / 빌드
1. Unity Hub에서 이 폴더를 6000.3.11f1로 연다.
2. `Assets/_Project/Scenes/Race.unity`를 열고 Play.
3. 메뉴 **Racing → Setup Scene**: 씬·머티리얼을 코드(`Assets/_Project/Editor/RacingSetup.cs`)로 다시 생성.
4. 메뉴 **Racing → Build Windows**: `Builds/Windows/Racing.exe` 생성.

트랙 모양은 `RacingSetup.StreetCorners`(도시 블록 꼭짓점, 코너는 자동으로 둥글게 처리)로 정해지며, 도로·방호벽·빌딩은 `TrackBuilder`가 실행 시 생성한다.

## 개발용 실행 옵션
`-autostart -menustart N -autopilot -day -night -laps N -timescale N -shots <폴더> -shottimes 5,20 -quitafter 60`
