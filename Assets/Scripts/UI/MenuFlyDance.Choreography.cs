using UnityEngine;

// The choreography half of MenuFlyDance: what the fly does at each moment, as channel values (a
// FlyPose), with no Transforms touched. MenuFlyDance.cs owns the rig, turns a FlyPose into node
// rotations and keeps the fly on the table. Most settings are declared here and the leg angles there,
// to keep both files short; both halves read both.
// Split by responsibility so the timing of the dance can be read and tuned without the rig code.
public partial class MenuFlyDance
{
    [Header("Timing")]
    [Tooltip("Dance tempo. Every menu fly uses the same clock (Time.time), so equal tempos stay in sync.")]
    [SerializeField, Range(40f, 240f)] float beatsPerMinute = 118f;
    [Tooltip("Shifts this fly's dance (and its sitting fidgets) by this many beats against the others.")]
    [SerializeField] float beatOffset = 0f;
    [Tooltip("Seconds SetMode takes to blend between dancing and sitting.")]
    [SerializeField, Range(0.05f, 2f)] float blendDuration = 0.5f;
    [Tooltip("The mode the fly starts in.")]
    [SerializeField] Mode mode = Mode.Dance;

    [Header("Dance: head, abdomen, wings")]
    [Tooltip("Degrees the head nods down on each beat.")]
    [SerializeField, Range(0f, 40f)] float headNodAngle = 22f;
    [Tooltip("Degrees the head turns to watch a kick. Much more and the eyes meet the raised front legs.")]
    [SerializeField, Range(0f, 30f)] float headTurnAngle = 15f;
    [Tooltip("Degrees the abdomen swings side to side.")]
    [SerializeField, Range(0f, 40f)] float abdomenSwayAngle = 22f;
    [Tooltip("Degrees the abdomen tip lifts on the beat (and with each breath when sitting).")]
    [SerializeField, Range(0f, 40f)] float abdomenLiftAngle = 18f;
    [Tooltip("Degrees the wings stand open while dancing; they flick wider on each beat.")]
    [SerializeField, Range(0f, 60f)] float wingSpreadAngle = 35f;
    [Tooltip("Degrees the wing tips flick up on each beat, and twitch when sitting.")]
    [SerializeField, Range(0f, 45f)] float wingLiftAngle = 20f;
    [Tooltip("Degrees of each wing beat while buzzing in the spinning jump.")]
    [SerializeField, Range(0f, 60f)] float wingBuzzAngle = 35f;
    [Tooltip("Wing beats per second while buzzing. Near half the frame rate it strobes rather than blurs.")]
    [SerializeField, Range(1f, 30f)] float wingBuzzFrequency = 14f;

    [Header("Dance: body")]
    // The amplitudes are large on purpose: the menu sees an 18 cm fly from about a metre, and the first
    // tuning (3.5% bounce, 12 degree lean) read in renders as a fly standing still.
    [Tooltip("Height of the bounce on every beat, in fly lengths (head to wing tips).")]
    [SerializeField, Range(0f, 0.3f)] float hopHeight = 0.12f;
    [Tooltip("Height of the spinning jump in bar 4, in fly lengths.")]
    [SerializeField, Range(0f, 1f)] float bigHopHeight = 0.5f;
    [Tooltip("Degrees the whole body leans side to side.")]
    [SerializeField, Range(0f, 45f)] float bodyRollAngle = 22f;
    [Tooltip("How far the body slides side to side with the lean, in fly lengths.")]
    [SerializeField, Range(0f, 0.3f)] float swayShift = 0.08f;
    [Tooltip("Degrees the body pitches nose-up in the big jump.")]
    [SerializeField, Range(0f, 40f)] float bodyPitchAngle = 10f;

    [Header("Sit")]
    [Tooltip("Degrees the legs lift at the hip when sitting, raising the knees beside the body. Less props the belly up on the legs.")]
    [SerializeField, Range(0f, 60f)] float sitFemurLift = 40f;
    [Tooltip("Degrees the wings close from their rest V when sitting. Above about 4 the wing edges cross.")]
    [SerializeField, Range(0f, 6f)] float wingFoldAngle = 3f;
    [Tooltip("Breaths per minute while sitting.")]
    [SerializeField, Range(4f, 60f)] float breathsPerMinute = 16f;
    [Tooltip("How much the abdomen swells with each breath (0.04 is 4% wider and taller).")]
    [SerializeField, Range(0f, 0.15f)] float breathDepth = 0.04f;
    [Tooltip("Degrees the sitting fly turns its head to look around.")]
    [SerializeField, Range(0f, 30f)] float sitLookAngle = 15f;
    [Tooltip("Seconds between wing twitches while sitting.")]
    [SerializeField, Range(0.5f, 20f)] float twitchInterval = 3.5f;

    const int BeatsPerBar = 4;
    const int BarsPerPhrase = 4;
    // The last half beat of each bar crossfades into the next bar's move, so moves never cut.
    const float CrossfadeBeats = 0.5f;

    // One instant of the dance or the sit. Leg channels are 0..1 amounts (each drives several joints in
    // PoseLeg; wave is -1..1); every angle is in degrees; hop and shift are in fly lengths; buzz and
    // breath are 0..1.
    struct FlyPose
    {
        public float frontLeft, frontRight, wave, kickLeft, kickRight, stepLeft, stepRight, fold;
        public float roll, pitch, spin, hop, shift, nod, headTurn, abdomenSway, abdomenLift, breath;
        public float wingSpread, wingLift, buzz;

        public static FlyPose Lerp(in FlyPose a, in FlyPose b, float t)
        {
            float L(float x, float y) => x + (y - x) * t;
            return new FlyPose
            {
                frontLeft = L(a.frontLeft, b.frontLeft), frontRight = L(a.frontRight, b.frontRight),
                wave = L(a.wave, b.wave), kickLeft = L(a.kickLeft, b.kickLeft), kickRight = L(a.kickRight, b.kickRight),
                stepLeft = L(a.stepLeft, b.stepLeft), stepRight = L(a.stepRight, b.stepRight), fold = L(a.fold, b.fold),
                roll = L(a.roll, b.roll), pitch = L(a.pitch, b.pitch), spin = L(a.spin, b.spin), hop = L(a.hop, b.hop), shift = L(a.shift, b.shift),
                nod = L(a.nod, b.nod), headTurn = L(a.headTurn, b.headTurn), abdomenSway = L(a.abdomenSway, b.abdomenSway),
                abdomenLift = L(a.abdomenLift, b.abdomenLift), breath = L(a.breath, b.breath),
                wingSpread = L(a.wingSpread, b.wingSpread), wingLift = L(a.wingLift, b.wingLift), buzz = L(a.buzz, b.buzz),
            };
        }
    }

    // A 4-bar phrase: pump, kick, wave, hop-and-spin, one move per bar.
    FlyPose DancePose(float beat)
    {
        const float phraseBeats = BeatsPerBar * BarsPerPhrase;
        int phrase = Mathf.FloorToInt(beat / phraseBeats);
        float inPhrase = beat - phrase * phraseBeats;
        int bar = Mathf.Min((int)(inPhrase / BeatsPerBar), BarsPerPhrase - 1);
        float inBar = inPhrase - bar * BeatsPerBar;
        FlyPose pose = Move(bar, inBar, phrase);
        float crossfade = Mathf.SmoothStep(0f, 1f, (inBar - (BeatsPerBar - CrossfadeBeats)) / CrossfadeBeats);
        if (crossfade <= 0f)
            return pose;
        int next = bar + 1;
        // The next move is read at a negative beat (just before its bar); every move handles that.
        FlyPose following = Move(next % BarsPerPhrase, inBar - BeatsPerBar, phrase + next / BarsPerPhrase);
        return FlyPose.Lerp(pose, following, crossfade);
    }

    FlyPose Move(int bar, float b, int phrase)
    {
        switch (bar)
        {
            case 0: return Pump(b);
            case 1: return Kick(b);
            case 2: return Wave(b);
            default: return HopSpin(b, (phrase & 1) == 0 ? 1f : -1f);
        }
    }

    // Shared by every bar: the head snaps down on each beat, the wings flick open with it, each little
    // bounce lands on the beat, and the front legs hang a little raised, ready to pump.
    FlyPose Groove(float b)
    {
        float accent = Accent(b);
        return new FlyPose
        {
            frontLeft = 0.2f, frontRight = 0.2f,
            hop = hopHeight * Arc(Mathf.Repeat(b, 1f), 0.5f, 0.5f),
            nod = headNodAngle * accent,
            abdomenLift = abdomenLiftAngle * accent,
            wingSpread = wingSpreadAngle * (0.6f + 0.4f * accent),
            wingLift = wingLiftAngle * accent,
        };
    }

    // Bar 1, pump: the front legs punch up in turn, one per beat, while the body rocks with them and
    // the abdomen swings across over two beats.
    FlyPose Pump(float b)
    {
        FlyPose p = Groove(b);
        float c = Mathf.Cos(Mathf.PI * b);
        // An odd cubic of the cosine: steeper through the middle and flat at the ends, so the leg snaps
        // up and holds, a punch where a plain sine floats. (A square root snapped harder but swung the
        // knee ~35 degrees in one frame at the crossing, which read as a pop.)
        float punch = c * (1.5f - 0.5f * c * c);
        p.frontLeft = 0.5f + 0.5f * punch;
        p.frontRight = 0.5f - 0.5f * punch;
        p.roll = 0.6f * bodyRollAngle * Mathf.Sin(Mathf.PI * b);
        p.shift = 0.6f * swayShift * Mathf.Sin(Mathf.PI * b);
        p.abdomenSway = abdomenSwayAngle * Mathf.Sin(Mathf.PI * b);
        return p;
    }

    // Bar 2, kick: the middle legs kick out in turn (left on even beats), the body leaning away and the
    // head turning to watch; both front legs pump each beat and the other hind leg steps on the off-beat.
    FlyPose Kick(float b)
    {
        FlyPose p = Groove(b);
        int beatIndex = Mathf.FloorToInt(b);
        float f = b - beatIndex;
        float kick = Hump(f, 0.7f);
        float step = Hump(f - 0.5f, 0.4f);
        bool isLeft = (beatIndex & 1) == 0;
        float side = isLeft ? -1f : 1f;
        if (isLeft) { p.kickLeft = kick; p.stepRight = step; }
        else { p.kickRight = kick; p.stepLeft = step; }
        p.roll = -side * 0.7f * bodyRollAngle * kick;   // a positive roll leans right: away from a left kick
        p.shift = -side * 0.5f * swayShift * kick;
        p.headTurn = -side * headTurnAngle * kick;       // a positive turn looks left
        p.frontLeft = p.frontRight = 0.65f + 0.35f * Mathf.Cos(2f * Mathf.PI * b);
        return p;
    }

    // Bar 3, wave: both front legs high, swaying overhead together as the whole body rocks with them
    // over two beats; the abdomen wiggles twice as fast and the hind legs step in turn.
    FlyPose Wave(float b)
    {
        FlyPose p = Groove(b);
        float sway = Mathf.Sin(Mathf.PI * b);
        p.frontLeft = p.frontRight = 0.9f;
        p.wave = sway;
        p.roll = bodyRollAngle * sway;
        p.shift = swayShift * sway;
        p.abdomenSway = abdomenSwayAngle * Mathf.Sin(2f * Mathf.PI * b);
        int beatIndex = Mathf.FloorToInt(b);
        float step = Hump(b - beatIndex, 0.5f);
        if ((beatIndex & 1) == 0) p.stepLeft = step;
        else p.stepRight = step;
        return p;
    }

    // Bar 4, hop and spin: little hops on beats 1 and 2, then a jump on beat 3 that turns a full circle
    // with the wings buzzing, landing half a beat before the phrase repeats. Direction alternates.
    FlyPose HopSpin(float b, float direction)
    {
        FlyPose p = Groove(b);
        float littleHops = Arc(b, 0f, 0.5f) + Arc(b, 1f, 0.5f);
        float jump = Arc(b, 2f, 1.5f);
        float spin = Mathf.Clamp01((b - 2f) / 1.5f);
        p.hop = 2f * hopHeight * littleHops + bigHopHeight * jump;
        // A full turn ends where it began, so the angle drops back to 0 once done; the crossfade into
        // the next bar then has nothing to unwind.
        p.spin = spin < 1f ? direction * 360f * SmootherStep(spin) : 0f;
        p.buzz = Mathf.Clamp01(3f * Mathf.Sin(Mathf.PI * spin));
        p.pitch = -bodyPitchAngle * jump;
        p.frontLeft = p.frontRight = 0.5f + 0.5f * Mathf.Max(littleHops, jump);
        p.kickLeft = p.kickRight = 0.5f * jump;
        return p;
    }

    // Sitting: legs folded, body down on the table, wings closed, breathing slowly; now and then a wing
    // twitches and the head looks about. Clocked in seconds: a resting fly does not keep the beat.
    FlyPose SitPose(float seconds)
    {
        float twitch = Hump(Mathf.Repeat(seconds, twitchInterval), 0.3f);
        // Two unrelated periods, so the looking around does not repeat in an obvious rhythm; the clamp
        // makes the head hold each way before turning back.
        float look = Mathf.Clamp(1.5f * Mathf.Sin(2f * Mathf.PI * seconds / 7.3f) + 0.7f * Mathf.Sin(2f * Mathf.PI * seconds / 3.1f), -1f, 1f);
        float breath = 0.5f - 0.5f * Mathf.Cos(2f * Mathf.PI * seconds * breathsPerMinute / 60f);
        return new FlyPose
        {
            fold = 1f,
            breath = breath,
            abdomenLift = 0.3f * abdomenLiftAngle * breath,
            nod = 0.3f * headNodAngle,
            headTurn = sitLookAngle * look,
            wingSpread = -wingFoldAngle + 0.5f * wingLiftAngle * twitch,
            wingLift = wingLiftAngle * twitch,
        };
    }

    // 1 on the beat, falling smoothly to 0 before mid-beat: sharp, but with no jump.
    static float Accent(float b) => Mathf.Pow(0.5f + 0.5f * Mathf.Cos(2f * Mathf.PI * b), 6f);
    // 0..1..0 parabola over [start, start + duration], 0 outside: one hop.
    static float Arc(float b, float start, float duration)
    {
        float x = (b - start) / duration;
        return x > 0f && x < 1f ? 4f * x * (1f - x) : 0f;
    }
    // 0..1..0 sine hump over [0, length], 0 outside: one kick, step or twitch.
    static float Hump(float f, float length) => f > 0f && f < length ? Mathf.Sin(Mathf.PI * f / length) : 0f;
    static float SmootherStep(float x) => x * x * x * (x * (6f * x - 15f) + 10f);
}
