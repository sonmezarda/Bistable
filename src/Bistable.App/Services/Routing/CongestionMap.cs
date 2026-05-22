namespace Bistable.App.Services.Routing;

internal sealed class CongestionMap
{
    private readonly Dictionary<long, int> _counts = [];

    public int Get(int column, int row) => _counts.GetValueOrDefault(Pack(column, row));

    public void Increment(int column, int row)
    {
        long key = Pack(column, row);
        _counts[key] = _counts.GetValueOrDefault(key) + 1;
    }

    public void Decrement(int column, int row)
    {
        long key = Pack(column, row);
        int count = _counts.GetValueOrDefault(key);
        if (count <= 1)
        {
            _counts.Remove(key);
        }
        else
        {
            _counts[key] = count - 1;
        }
    }

    private static long Pack(int column, int row) => ((long)column << 32) | (uint)row;
}
