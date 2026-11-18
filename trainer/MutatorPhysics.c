/* Runtime-only per-vehicle mutators for the supported Windows x86 build.
 * Shared car definitions, saved tuning and global physics data stay untouched.
 */
typedef unsigned int U32;
/* A two-register fastcall has the same ECX/no-stack-argument contract here;
 * the original thiscall method treats EDX as a volatile register. */
typedef void (__fastcall *OriginalTick)(void *client, void *unused);
int _fltused = 1;

enum { Parameters = 7, MaxFrames = 128 };
enum { Acceleration, TopSpeed, NitroPower, AirRotation, DriftRadius, Steering, Braking };

typedef struct { float x, y, z; } Vector;
typedef struct
{
    U32 car, body, rigid;
    float previousRotation;
    Vector unscaledRotation;
} VehicleFrame;

typedef struct
{
    U32 module, trampoline;
    volatile U32 mask[2];
    volatile float factor[2][Parameters];
    U32 calls[3][2];
    U32 usedMask[2];
    float before[2][Parameters], after[2][Parameters];
    U32 applications[2][Parameters];
    VehicleFrame frame[MaxFrames];
} MutatorState;

typedef char VectorSizeCheck[sizeof(Vector) == 12 ? 1 : -1];
typedef char FrameSizeCheck[sizeof(VehicleFrame) == 28 ? 1 : -1];

static __forceinline U32 word(U32 address, U32 offset)
{
    return *(U32 *)(address + offset);
}

static __forceinline float *number(U32 address, U32 offset)
{
    return (float *)(address + offset);
}

static __forceinline int finite(float value)
{
    union { float f; U32 u; } bits;
    bits.f = value;
    return (bits.u & 0x7f800000u) != 0x7f800000u;
}

static __forceinline float absf(float value) { return value < 0 ? -value : value; }

static __forceinline float bounded(float value)
{
    if (!finite(value)) return 0;
    return value > 1000000.0f ? 1000000.0f : value < -1000000.0f ? -1000000.0f : value;
}

static __forceinline float factor(MutatorState *state, int target, int parameter)
{
    float value = state->factor[target][parameter];
    return finite(value) && value >= 0 && value <= 100 ? value : 1;
}

static __forceinline int enabled(MutatorState *state, int target, int parameter)
{
    return (state->mask[target] & (1u << parameter)) != 0;
}

static __forceinline void record(MutatorState *state, int target, int parameter, float before, float after)
{
    state->usedMask[target] |= 1u << parameter;
    state->before[target][parameter] = before;
    state->after[target][parameter] = after;
    ++state->applications[target][parameter];
}

static __forceinline void scale(MutatorState *state, int target, int parameter, float *value)
{
    if (enabled(state, target, parameter))
    {
        float original = *value;
        *value = bounded(original * factor(state, target, parameter));
        /* A near-zero speed cap stops the car without introducing a zero
         * divisor into the stock speed/RPM calculations. */
        if (parameter == TopSpeed && *value < 0.01f) *value = 0.01f;
        record(state, target, parameter, original, *value);
    }
}

static __forceinline int airborne(U32 body)
{
    U32 begin = word(body, 0x50), end = word(body, 0x54), at;
    if (!begin || end <= begin || end - begin > 8 * sizeof(U32) || ((end - begin) & 3))
        return 0;
    for (at = begin; at < end; at += sizeof(U32))
    {
        U32 wheel = *(U32 *)at;
        if (!wheel || word(wheel, 0x44)) return 0;
    }
    return 1;
}

static __forceinline VehicleFrame *getFrame(MutatorState *state, U32 car, U32 body, U32 rigid)
{
    U32 first = (car >> 4) & (MaxFrames - 1), i;
    VehicleFrame *empty = 0;
    for (i = 0; i < MaxFrames; ++i)
    {
        VehicleFrame *frame = &state->frame[(first + i) & (MaxFrames - 1)];
        if (frame->car == car)
        {
            if (frame->body == body && frame->rigid == rigid) return frame;
            empty = frame;
            break;
        }
        if (!frame->car && !empty) empty = frame;
    }
    /* A finished race can leave stale addresses in this private cache. Reusing a
     * slot never dereferences its previous car or rigid-body allocation. */
    if (!empty) empty = &state->frame[first];
    empty->car = car;
    empty->body = body;
    empty->rigid = rigid;
    empty->previousRotation = 1;
    empty->unscaledRotation.x = empty->unscaledRotation.y = empty->unscaledRotation.z = 0;
    return empty;
}

static __forceinline Vector readRotation(U32 rigid)
{
    Vector result;
    result.x = *number(rigid, 0x160);
    result.y = *number(rigid, 0x164);
    result.z = *number(rigid, 0x168);
    return result;
}

static __forceinline void writeRotation(U32 rigid, Vector value)
{
    *number(rigid, 0x160) = bounded(value.x);
    *number(rigid, 0x164) = bounded(value.y);
    *number(rigid, 0x168) = bounded(value.z);
    *number(rigid, 0x16c) = 0; /* btVector3's fourth lane matches the native setter. */
}

static void updateVehicle(U32 pc, MutatorState *state, int target)
{
    U32 body = word(pc, 0x34), wrapper = body ? word(body, 4) : 0;
    U32 rigid = wrapper ? word(wrapper, 0x58) : 0;
    VehicleFrame *frame = 0;
    float previous = 1;
    U32 original = state->trampoline;
    if (rigid)
    {
        frame = getFrame(state, pc - 0xec, body, rigid);
        previous = frame->previousRotation;
        if (previous != 1)
        {
            Vector value = readRotation(rigid);
            if (previous > 0.000001f && airborne(body))
            {
                value.x /= previous;
                value.y /= previous;
                value.z /= previous;
            }
            /* At touchdown the contact solver may have introduced a fresh
             * impulse. Restore the cached unscaled baseline instead of
             * magnifying that impulse by dividing by a very small factor. */
            else value = frame->unscaledRotation;
            writeRotation(rigid, value);
        }
        frame->previousRotation = 1;
    }
    /* The original method takes its PhysicsClient in ECX and no stack args. */
    ((OriginalTick)original)((void *)pc, 0);

    if (frame && word(pc, 0x34) == body && word(body, 4) == wrapper &&
        word(wrapper, 0x58) == rigid && enabled(state, target, AirRotation) && airborne(body))
    {
        float multiplier = factor(state, target, AirRotation);
        Vector value = readRotation(rigid);
        float before = absf(value.x) + absf(value.y) + absf(value.z);
        frame->unscaledRotation = value;
        value.x *= multiplier;
        value.y *= multiplier;
        value.z *= multiplier;
        writeRotation(rigid, value);
        frame->previousRotation = multiplier;
        record(state, target, AirRotation, before,
               absf(value.x) + absf(value.y) + absf(value.z));
    }
}

void __cdecl ApplyMutators(void *client, MutatorState *state, int stage)
{
    U32 pc = (U32)client, car = pc - 0xec;
    U32 kind = word(car, 0x134);
    int target;
    if (kind > 1)
    {
        if (stage == 0)
        {
            U32 original = state->trampoline;
            ((OriginalTick)original)((void *)pc, 0);
        }
        return;
    }
    target = kind == 1 ? 0 : 1;
    ++state->calls[stage][target];
    if (stage == 0)
    {
        /* Respect the original inactive/uninitialized vehicle guards before
         * following any of its transient physics pointers. */
        if ((*(unsigned char *)(car + 0x14) & 4) && *(unsigned char *)(pc + 0xa9))
            updateVehicle(pc, state, target);
        else ((OriginalTick)state->trampoline)((void *)pc, 0);
    }
    else if (stage == 1)
    {
        scale(state, target, Acceleration, number(pc, 0x80));
        scale(state, target, TopSpeed, number(pc, 0x88));
        scale(state, target, NitroPower, number(pc, 0x8c));
        scale(state, target, Steering, number(pc, 0x78));
        /* The second steering result drives the matching wheel presentation. */
        if (enabled(state, target, Steering))
            *number(pc, 0x7c) = bounded(*number(pc, 0x7c) * factor(state, target, Steering));
        scale(state, target, Braking, number(pc, 0x84));
    }
    else if (stage == 2 && enabled(state, target, DriftRadius))
    {
        U32 body = word(pc, 0x34);
        float radius = factor(state, target, DriftRadius);
        float original = *number(body, 0x294);
        float value = original / (radius < 0.01f ? 0.01f : radius);
        /* Native drift assist builds a quaternion from this yaw angle. */
        value = value > 3.14159265f ? 3.14159265f : value < -3.14159265f ? -3.14159265f : value;
        *number(body, 0x294) = value;
        record(state, target, DriftRadius, original, value);
    }
}
