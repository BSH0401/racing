# Racing

Unity 6 (6000.3.11f1, URP) 3D 서킷 레이싱 게임 프로토타입.

## 게임 내용
- 언덕이 있는 오픈 도심 맵: 9×9 도로 격자(블록 96m), 4차선 도로·중앙선·횡단보도·연석·보도, 빌딩 블록과 공원, 가로등, 도시 외곽 방벽
- GTA 스트리트 레이스 방식: 도시를 도는 약 2.6km 경로에 체크포인트 24개, 순서대로 통과(지름길 가능), 다음 체크포인트 광선 표시
- 플레이어를 따라 회전하는 레이더식 미니맵(경로선·체크포인트 표시)
- 메인 화면: 배경에서 차들이 자동 주행하고 시네마틱 카메라가 중계 (START RACE / SETTINGS / CONTROLS / QUIT, 마우스 지원)
- 설정: 랩 수(1~10), 시간대(낮/밤), AI 난이도(EASY/NORMAL/HARD) — 저장됨
- 기록: 베스트 랩, 최고 순위 저장 및 메인 화면 표시
- 플레이어 1명 + AI 5명
- GTA5 스타일 차량 물리: 슬립각 타이어 모델, 서스펜션 하중 이동, 후륜 위주 구동 + 트랙션 컨트롤, 자동 카운터스티어·자세 안정화, 차체 롤/피치 연출, 핸드브레이크 드리프트
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

도시 지형·도로 격자는 `CityLayout`(높이 함수, 블록 크기), 레이스 경로는 `RacingSetup.RouteIntersections`(교차로 좌표)로 정해지며, 도시 메쉬는 `TrackBuilder`가 실행 시 생성한다.

## 개발용 실행 옵션
`-handlingtest` (평지에서 핸들링 측정 후 로그 출력·종료), `-logrespawns`, `-autostart -menustart N -autopilot -day -night -laps N -timescale N -shots <폴더> -shottimes 5,20 -quitafter 60`
