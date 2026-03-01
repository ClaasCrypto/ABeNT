using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using ABeNT.Model;

namespace ABeNT.Services
{
    public interface ISttService : IDisposable
    {
        Task<List<TranscriptSegment>> TranscribeAudioAsync(string filePath, RecorderReportOptions options);
    }
}
