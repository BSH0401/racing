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

        [System.NonSerialized] public int index;
        [System.NonSerialized] public int crossings;
        [System.NonSerialized] public int maxCrossings;
        [System.NonSerialized] public float lapStart;
        [System.NonSerialized] public float lastLap;
        [System.NonSerialized] public float bestLap;
        [System.NonSerialized] public bool finished;
        [System.NonSerialized] public float finishTime;
        [System.NonSerialized] public int position;
        [System.NonSerialized] public float stuckTimer, flipTimer, offTrackTimer, wrongWayTimer;

        public int CurrentLap(int laps) => Mathf.Clamp(maxCrossings, 1, laps);

        void Awake()
        {
            car = GetComponent<CarController>();
            ai = GetComponent<AIDriver>();
            driver = GetComponent<PlayerDriver>();
        }

        public void ResetProgress(int startIndex)
        {
            index = startIndex;
            crossings = maxCrossings = 0;
            lapStart = lastLap = finishTime = 0f;
            bestLap = -1f;
            finished = false;
            stuckTimer = flipTimer = offTrackTimer = wrongWayTimer = 0f;
        }

        public float Progress(TrackPath track) => crossings * track.Count + track.Rel(index);

        public string ColorHex => ColorUtility.ToHtmlStringRGB(color);
    }
}
