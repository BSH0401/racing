# Racing

Unity 6 (6000.3.11f1, URP) 3D 서킷 레이싱 게임 프로토타입.

## 게임 내용
- 언덕이 있는 오픈 도심 맵: 9×9 도로 격자(블록 96m), 4차선 도로·중앙선·횡단보도·연석·보도, 빌딩 블록과 공원, 가로등, 도시 외곽 방벽
- GTA 스트리트 레이스 방식: 도시를 도는 약 2.6km 경로에 체크포인트 24개, 순서대로 통과(지름길 가능), 다음 체크포인트 광선 표시
- 플레이어를 따라 회전하는 레이더식 미니맵(경로선·체크포인트 표시)
- 메인 화면: 배경에서 차들이 자동 주행하고 시네마틱 카메라가 중계 (START / MODE / GARAGE / SETTINGS / CONTROLS / QUIT, 마우스 지원)
- 게임 모드 (메인 화면 MODE에서 선택)
  - **RACE**: 체크포인트 레이스
  - **PURSUIT (추격)**: 도망치는 용의자 차를 150초 안에 들이받아 손상 게이지를 0으로 만들면 검거. 300m 이상 6초간 벌어지면 놓침. 내 차에 경광등, 용의자 위에 표시 광선
  - **ESCAPE (도주)**: 경찰차(경광등)가 도로망을 따라 추격, 20초마다 지원 경찰 합류(최대 5대). 90초 버티거나 350m 이상 5초간 따돌리면 성공, 경찰에 둘러싸여 멈추면 BUSTED 게이지가 차서 체포
  - 성공 시 1,500 CR + 남은 시간 보너스, 실패 시 200 CR
- 차고(GARAGE): 차량 15종의 스펙(최고속도·가속·그립·무게·구동)을 보고 선택. 레이스 순위에 따라 크레딧(CR)을 받아 잠긴 차를 구매·해금 (저장됨)
  | 차량 | 가격 | 특징 |
  |---|---|---|
  | BMW M3 E30 | 기본 | 균형 잡힌 후륜 |
  | Crown Victoria Taxi | 1,500 CR | 무겁고 느리지만 튼튼 |
  | Mazda RX-7 FC | 2,000 CR | 가볍고 싼 입문 드리프트카 |
  | Street SUV | 3,000 CR | 출발 가속 강함, 그립 낮음 |
  | Ford Mustang '65 | 4,000 CR | V8 머슬, 잘 미끄러짐 |
  | Mercedes G-Class | 4,500 CR | 무겁고 밀어붙이기 좋음 (4WD) |
  | Police Interceptor | 5,000 CR | 높은 최고속도 |
  | Charger Pursuit | 6,500 CR | 현대식 경찰 머슬 |
  | Toyota Supra A70 | 7,000 CR | 터보 GT |
  | Sport Coupe | 8,000 CR | 가볍고 날카로운 코너링 |
  | Mercedes 300 SL | 9,000 CR | 걸윙 클래식 GT |
  | Mazda RX-7 FD | 10,000 CR | 로터리, 민첩하고 빠름 |
  | Honda NSX | 11,000 CR | 미드십 코너링 |
  | Porsche 911 Turbo | 12,000 CR | 최고속도 최강, 뒤가 잘 미끄러짐 |
  | Nissan Skyline R34 | 14,000 CR | 4륜구동 그립 최강 |
- 상금: 1~6위 2,000/1,400/1,000/700/500/300 CR (2랩 기준, 랩 수에 비례). 레이스에 나오는 6대 외의 차는 예비 차체로 보관되다가 선택하면 교체된다. 도주 모드 경찰은 실제 경찰차(Charger·크라운 빅토리아)를 우선 사용
- 설정: 랩 수(1~10), 시간대(낮/밤), AI 난이도(EASY/NORMAL/HARD) — 저장됨
- 기록: 베스트 랩, 최고 순위 저장 및 메인 화면 표시
- 플레이어 1명 + AI 5명
- GTA5 스타일 차량 물리: 슬립각 타이어 모델, 서스펜션 하중 이동, 후륜 위주 구동 + 트랙션 컨트롤, 자동 카운터스티어·자세 안정화, 차체 롤/피치 연출, 핸드브레이크 드리프트
- 순위/랩/랩타임/베스트랩 HUD, 미니맵, 역주행 경고, 카운트다운, 결과 화면
- 낮/밤 테마: 밤에는 빌딩 창문 불빛, 가로등·차량 헤드라이트 실제 조명, 야간 하늘과 안개
- 실사풍 그래픽: 실사 외벽 사진(PBR) 빌딩, 아스팔트·보도블록 PBR 텍스처, HDRI 하늘(낮/밤)로 조명·반사, 소화전·쓰레기통·콘크리트 방호벽 소품, ACES 톤매핑·SSAO·블룸·모션블러
- 실사 차량 3D 모델(Sketchfab, CC BY 4.0): 플레이어 BMW M3 E30, AI 포르쉐 911 터보·스포츠카·택시·경찰차·SUV — 바퀴가 실제로 회전·조향
- 합성 사운드: 실린더 폭발음 기반 엔진(저·고회전 2개 루프 크로스페이드, 5단 기어, 변속 시 끊김, 가속 시 밝고 감속 시 먹먹하게), 차마다 음높이 다름, 타이어 스키드음, 고속 바람 소리, 충돌음, 경찰 사이렌. 설정에서 볼륨(0~10) 조절

## 조작
| 동작 | 키보드 | 게임패드 |
|---|---|---|
| 가속 / 브레이크·후진 | W·↑ / S·↓ | RT / LT |
| 조향 | A D · ← → | 왼쪽 스틱 |
| 핸드브레이크 | Space | A |
| 차량 리셋 | R | Y |
| 시점 회전 (손 떼면 자동 복귀) | 마우스 | 오른쪽 스틱 |
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
`-handlingtest` (평지에서 핸들링 측정 후 로그 출력·종료), `-showcase <폴더>` (차량별 측면·전면·후면 스크린샷 후 종료, `-showall`이면 차고 15종 전부), `-mode race|pursuit|escape`, `-logchase`, `-car <id>` (플레이어 차 강제), `-garage N` (차고 화면 N번 차로 열기), `-credits N`, `-lookyaw N` (자유 시점 각도 고정), `-logrespawns`, `-autostart -menustart N -autopilot -day -night -laps N -timescale N -shots <폴더> -shottimes 5,20 -quitafter 60`

## 크레딧
- 차량 모델 (CC BY 4.0, 크기·방향·바퀴 분리 등 게임용으로 변환, 텍스처 축소):
  - "[[FREE] BMW M3 E30](https://sketchfab.com/3d-models/free-bmw-m3-e30-ac3c7013434e403e8faff87948caf422)" by [Martin Trafas](https://sketchfab.com/Bexxie)
  - "[Porsche 911 (930) Turbo 1975](https://sketchfab.com/3d-models/porsche-911-930-turbo-1975-de1ffd344c41481892511f7fd332c136)" by [Lexyc16](https://sketchfab.com/Lexyc16)
  - "[Generic passenger car pack](https://sketchfab.com/3d-models/generic-passenger-car-pack-20f9af9b8a404d5cb022ac6fe87f21f5)" by [Comrade1280](https://sketchfab.com/comrade1280) (스포츠카·SUV)
  - "[2001 Crown Victoria Taxi Game Prop](https://sketchfab.com/3d-models/2001-crown-victoria-taxi-game-prop-2429a825ff1646568e8e6e1453f9c501)" by [8sianDude](https://sketchfab.com/haoliu95)
  - "[2001 Crown Victoria Police Interceptor Game Prop](https://sketchfab.com/3d-models/2001-crown-victoria-police-interceptor-game-prop-9f30d360cee343efb5a441978ddb57bd)" by [8sianDude](https://sketchfab.com/haoliu95)
  - "[Mazda RX-7 FD](https://sketchfab.com/3d-models/mazda-rx-7-fd-d35ff630df614771b82e7b2f59035b1e)", "[Mazda RX-7 FC](https://sketchfab.com/3d-models/mazda-rx-7-fc-8ac0df459f514950ab83ac37109a06ab)", "[Nissan Skyline R34 GT-R](https://sketchfab.com/3d-models/nissan-skyline-r34-gt-r-ff8fb2251dfa4bb9979e7022c5a6666c)", "[Honda NSX 1990](https://sketchfab.com/3d-models/honda-nsx-1990-1cc15628a00a4739a6b6c01128927c8d)", "[Mercedes-Benz 300 SL Gullwing](https://sketchfab.com/3d-models/mercedes-benz-300-sl-gullwing-505241c829c540a4921533000736904e)", "[Mercedes Benz G-class W263](https://sketchfab.com/3d-models/mercedes-benz-g-class-w263-1a2a52b16cad4e618af347461817895c)" by [Lexyc16](https://sketchfab.com/Lexyc16)
  - "[1965 Ford Mustang](https://sketchfab.com/3d-models/1965-ford-mustang-7e93cc22b96a4788897a513374ef4486)", "[Dodge Charger Police NYPD](https://sketchfab.com/3d-models/dodge-charger-police-nypd-578e2bfb9c8f415d932527a938e2fc64)" by [David_Holiday](https://sketchfab.com/David_Holiday)
  - "[Toyota Supra 2.5 GT Twin Turbo (A70)](https://sketchfab.com/3d-models/toyota-supra-25-gt-twin-turbo-a70-6d002956a62a4e1085c6aab41b088ab1)" by [GT Cars: Hyperspeed](https://sketchfab.com/Car2022)
  - 라이선스: [CC BY 4.0](http://creativecommons.org/licenses/by/4.0/), 원본 정보는 `Assets/_Project/ThirdParty/Sketchfab/*/license.txt`
- PBR 텍스처(도로·보도·건물 외벽): [ambientCG](https://ambientcg.com) (CC0)
- HDRI 하늘·거리 소품: [Poly Haven](https://polyhaven.com) (CC0)
- 라이선스 파일: `Assets/_Project/ThirdParty/*/License.txt` (Sketchfab은 `license.txt`)
