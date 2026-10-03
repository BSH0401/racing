using UnityEngine;

namespace Racing
{
    // Race bookkeeping for one car. RaceManager updates the progress fields.
    public class Racer : MonoBehaviour
    {
        public string racerName = "Racer";
        public Color color = Color.white;
        public bool isPlayer;

        [System.NonSerialized] public CarController car;
        [System.NonSerialized] public AIDriver ai;
        [System.NonSerialized] public PlayerDriver driver;

        // Nearest route sample (used by the AI and for respawns).
        [System.NonSerialized] public int index;
        // Checkpoints passed since the start; the start line itself is checkpoint 0.
        [System.NonSerialized] public int cpPassed;
        [System.NonSerialized] public int lastCp;
        [System.NonSerialized] public float lapStart;
        [System.NonSerialized] public float lastLap;
        [System.NonSerialized] public float bestLap;
        [System.NonSerialized] public bool finished;
        [System.NonSerialized] public float finishTime;
        [System.NonSerialized] public int position;
        [System.NonSerialized] public float progress;
        [System.NonSerialized] public float stuckTimer, flipTimer, offTrackTimer, wrongWayTimer, reverseTimer;
        [System.NonSerialized] public int respawns;

        public int LapsDone(int checkpoints) => cpPassed <= 0 ? 0 : (cpPassed - 1) / checkpoints;
        public int CurrentLap(int laps, int checkpoints) => Mathf.Clamp(LapsDone(checkpoints) + 1, 1, laps);

        void Awake()
        {
            car = GetComponent<CarController>();
            ai = GetComponent<AIDriver>();
            driver = GetComponent<PlayerDriver>();
        }

        public void ResetProgress(int startIndex)
        {
            index = startIndex;
            cpPassed = 0;
            lastCp = 0;
            lapStart = lastLap = finishTime = progress = 0f;
            bestLap = -1f;
            finished = false;
            stuckTimer = flipTimer = offTrackTimer = wrongWayTimer = reverseTimer = 0f;
            respawns = 0;
        }

        public string ColorHex => ColorUtility.ToHtmlStringRGB(color);
    }
}
