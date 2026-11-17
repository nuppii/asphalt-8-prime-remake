typedef unsigned int U32;
int _fltused = 1;

enum { MaxRacers = 61 };

typedef struct
{
    float x, y, z;
} Position;

typedef struct
{
    U32 vtable;
    U32 beforeId[3];
    U32 id;
    U32 beforePosition[6];
    Position position;
    U32 afterPosition[16];
} GridMarker;

typedef char GridMarkerSizeCheck[sizeof(GridMarker) == 0x78 ? 1 : -1];

typedef struct
{
    U32 randomState;
    U32 sequence;
    int count, nativeCount, lanes, playerRank;
    float laneWidth, rowPitch;
    GridMarker *order[MaxRacers];
} GridState;

static __forceinline float absf(float value)
{
    return value < 0 ? -value : value;
}

static __forceinline float minf(float a, float b)
{
    return a < b ? a : b;
}

static __forceinline int truncateFloat(float value)
{
    int result;
    __asm { cvttss2si eax, value }
    __asm { mov result, eax }
    return result;
}

static __forceinline Position between(Position a, Position b, float t)
{
    Position p;
    p.x = a.x + (b.x - a.x) * t;
    p.y = a.y + (b.y - a.y) * t;
    p.z = a.z + (b.z - a.z) * t;
    return p;
}

static __forceinline void copyMarker(GridMarker *out, const GridMarker *in)
{
    U32 *destination = (U32 *)out;
    const U32 *source = (const U32 *)in;
    int i;
    for (i = 0; i < sizeof(GridMarker) / sizeof(U32); ++i)
        destination[i] = source[i];
}

static __forceinline Position sampleLane(GridMarker **markers, int count, int lane, float offset, float spacing)
{
    float sample = (offset / spacing - (float)lane) * 0.5f;
    int last = (count - 1 - lane) / 2;
    int index = sample < 0 ? 0 : sample >= (float)last ? last - 1 : truncateFloat(sample);
    float t = sample - (float)index;
    return between(markers[index * 2 + lane]->position, markers[(index + 1) * 2 + lane]->position, t);
}

static __forceinline unsigned draw(GridState *state, unsigned count)
{
    unsigned limit = 0xffffffffu - 0xffffffffu % count;
    unsigned value, bits = state->randomState;
    do
    {
        bits ^= bits << 13;
        bits ^= bits >> 17;
        bits ^= bits << 5;
        value = bits - 1;
    }
    while (value >= limit);
    state->randomState = bits;
    return value % count;
}

// The caller's native reversal reserves the final entry for the player.
// Rotate that entry out of the ordered sequence and preserve the order of the AI.
void __cdecl PrepareGrid(GridMarker **begin, GridMarker **end, GridMarker *generated,
                         GridState *state, U32 markerVtable)
{
    GridMarker *stock[MaxRacers];
    GridMarker *selected;
    int count = (int)(end - begin);
    int native = 0, lanes = 0, i, rank;
    float width = 0, pitch = 0;
    if (count < 1 || count > MaxRacers)
        return;
    while (native < count && begin[native])
    {
        stock[native] = begin[native];
        ++native;
    }
    if (!native)
        return;

    if (native < count && native >= 4)
    {
        int rows, lastLeft = (native - 1) & ~1;
        float span, spacing, start, finish, dx, dy, lengthSquared, widthSquared, remaining;
        Position front = stock[0]->position;
        Position back = stock[lastLeft]->position;
        dx = back.x - front.x;
        dy = back.y - front.y;
        lengthSquared = dx * dx + dy * dy;
        widthSquared = 100.0f;
        for (i = 0; i < native && stock[i]->vtable == markerVtable; ++i)
        {
            if ((i & 1) != 0)
            {
                Position a = stock[i - 1]->position, b = stock[i]->position;
                float cross = (b.x - a.x) * dy - (b.y - a.y) * dx;
                if (lengthSquared > 1.0f)
                    widthSquared = minf(widthSquared, cross * cross / lengthSquared);
            }
        }
        if (i == native && lengthSquared > 1.0f && widthSquared > 1.0f)
        {
            // The x87 instruction avoids a CRT dependency in the freestanding code.
            __asm { fld lengthSquared }
            __asm { fsqrt }
            __asm { fstp lengthSquared }
            __asm { fld widthSquared }
            __asm { fsqrt }
            __asm { fstp width }
            span = lengthSquared;
            spacing = span / (float)lastLeft;
            lanes = width >= 5.4f ? 3 : 2;
            rows = (count + lanes - 1) / lanes;
            // Keep every row inside the longitudinal extent of the stock grid.
            start = spacing * 0.20f;
            finish = spacing * ((float)(native - 1) - 0.25f);
            remaining = finish - start - 0.50f;
            pitch = remaining / (float)(rows - 1);
            for (i = 0; i < count; ++i)
            {
                float across = (float)(i % lanes) / (float)(lanes - 1);
                float offset = start + (float)(i / lanes) * pitch + across * 0.50f;
                Position left = sampleLane(stock, native, 0, offset, spacing);
                Position right = sampleLane(stock, native, 1, offset, spacing);
                int orientation = truncateFloat(offset / spacing + 0.5f);
                if (orientation >= native)
                    orientation = native - 1;
                copyMarker(&generated[i], stock[orientation]);
                // IDs are internal track entity IDs. The synthetic range is disjoint from stock entities.
                generated[i].id = 0x40000000u + (U32)i;
                generated[i].position = between(left, right, across);
                begin[i] = &generated[i];
            }
        }
    }

    // Preserve the former fallback for a track without a usable two-lane grid.
    // This protects the engine from a null marker on an unsupported geometry.
    if (native < count && !lanes)
        for (i = native; i < count; ++i)
            begin[i] = stock[i % native];

    rank = (int)draw(state, (unsigned)count);
    state->count = count;
    state->nativeCount = native;
    state->lanes = lanes;
    state->playerRank = rank + 1;
    state->laneWidth = width;
    state->rowPitch = pitch;
    for (i = 0; i < count; ++i)
        state->order[i] = begin[i];
    selected = begin[rank];
    for (i = rank; i < count - 1; ++i)
        begin[i] = begin[i + 1];
    begin[count - 1] = selected;
    ++state->sequence;
}
