namespace PlexBuster.Data
{
    /// <summary>
    /// Works around Windows' Matroska reader on the streams Plex converts. Plex marks progressive video with
    /// FlagInterlaced = 2 ("progressive" in today's Matroska spec). The Windows reader follows the older spec, where
    /// any non-zero value means interlaced, and so shows every other frame (12 fps for 24 fps films). Setting the
    /// flag to 0 ("undetermined") keeps the header the same size, so nothing after it moves.
    /// </summary>
    public static class MatroskaHeaderFix
    {
        const uint EbmlHeader = 0x1A45DFA3;
        const uint Segment = 0x18538067;
        const uint Tracks = 0x1654AE6B;
        const uint TrackEntry = 0xAE;
        const uint Video = 0xE0;
        const uint FlagInterlaced = 0x9A;
        const uint Cluster = 0x1F43B675;

        /// <summary>Patches the first <paramref name="length"/> bytes of a stream in place; true if it changed anything.</summary>
        public static bool Apply(byte[] data, int length)
        {
            var i = 0;
            if (!Read(data, length, ref i, out var id, out var size, out _) || id != EbmlHeader) return false;
            i += (int)size;
            if (!Read(data, length, ref i, out id, out size, out var unknown) || id != Segment) return false;
            var segmentEnd = unknown ? length : (int)System.Math.Min(length, i + size);

            var changed = false;
            while (i < segmentEnd && Read(data, segmentEnd, ref i, out id, out size, out unknown))
            {
                if (id == Cluster || unknown) break;
                var end = (int)System.Math.Min(segmentEnd, i + size);
                if (id == Tracks) changed |= FixTracks(data, i, end);
                i = end;
            }
            return changed;
        }

        static bool FixTracks(byte[] data, int i, int end)
        {
            var changed = false;
            while (i < end && Read(data, end, ref i, out var id, out var size, out _))
            {
                var elementEnd = (int)System.Math.Min(end, i + size);
                if (id == TrackEntry || id == Video) changed |= FixTracks(data, i, elementEnd);
                else if (id == FlagInterlaced && size == 1 && data[i] == 2)
                {
                    data[i] = 0;
                    changed = true;
                }
                i = elementEnd;
            }
            return changed;
        }

        /// <summary>Reads an element's id and size; <paramref name="i"/> moves to the start of its data.</summary>
        static bool Read(byte[] data, int end, ref int i, out uint id, out long size, out bool unknown)
        {
            id = 0;
            size = 0;
            unknown = false;
            if (i >= end) return false;

            var idLength = LeadingLength(data[i], 4);
            if (idLength == 0 || i + idLength > end) return false;
            for (var k = 0; k < idLength; k++) id = (id << 8) | data[i + k];
            i += idLength;

            if (i >= end) return false;
            var sizeLength = LeadingLength(data[i], 8);
            if (sizeLength == 0 || i + sizeLength > end) return false;
            size = data[i] & (0xFF >> sizeLength);
            var allOnes = size == (0xFF >> sizeLength);
            for (var k = 1; k < sizeLength; k++)
            {
                size = (size << 8) | data[i + k];
                allOnes &= data[i + k] == 0xFF;
            }
            unknown = allOnes;
            i += sizeLength;
            return true;
        }

        /// <summary>EBML variable-length integers: the position of the first set bit gives the length in bytes.</summary>
        static int LeadingLength(byte first, int max)
        {
            for (var length = 1; length <= max; length++)
                if ((first & (0x80 >> (length - 1))) != 0) return length;
            return 0;
        }
    }
}
