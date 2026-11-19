namespace NwcIO.Internal.Container;

// Blowfish block operations follow Schneier's published algorithm.
// https://www.schneier.com/academic/archives/1994/09/description_of_a_new.html
internal sealed class BlowfishBlock
{
    private const int Rounds = 16;
    private const int Subkeys = Rounds + 2;
    private readonly uint[] _table = BlowfishPi.Words.ToArray();

    public BlowfishBlock(ReadOnlySpan<byte> key)
    {
        int position = 0;
        for (int i = 0; i < Subkeys; i++)
        {
            uint word = 0;
            for (int j = 0; j < sizeof(uint); j++)
            {
                word = (word << 8) | key[position];
                position = (position + 1) % key.Length;
            }

            _table[i] ^= word;
        }

        uint left = 0, right = 0;
        for (int i = 0; i < _table.Length; i += 2)
        {
            (left, right) = Encrypt(left, right);
            _table[i] = left;
            _table[i + 1] = right;
        }
    }

    public (uint Left, uint Right) Encrypt(uint left, uint right)
    {
        unchecked
        {
            for (int i = 0; i < Rounds; i++)
            {
                left ^= _table[i];
                uint first = _table[Subkeys + (left >> 24)];
                uint second = _table[Subkeys + 256 + ((left >> 16) & 255)];
                uint third = _table[Subkeys + 512 + ((left >> 8) & 255)];
                uint fourth = _table[Subkeys + 768 + (left & 255)];
                right ^= ((first + second) ^ third) + fourth;
                (left, right) = (right, left);
            }

            return (right ^ _table[Rounds + 1], left ^ _table[Rounds]);
        }
    }
}
