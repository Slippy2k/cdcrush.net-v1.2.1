﻿using System;
using System.IO;
using System.Text.RegularExpressions;

namespace cdcrush.lib.app
{

/// <summary>
/// Simple wrapper for FFmpeg
/// Currently just supports Audio Compression for use with the CDCRUSH project
/// 
///		"onProgress" => Reports percentage 0 to 100
///		"onComplete" => Exit code 0 for OK, other for ERROR
///	
/// </summary>
class FFmpeg:ICliReport
{
	const string EXECUTABLE_NAME = "ffmpeg.exe";
	private CliApp proc;

	// # USER SET ::
	public Action<int> onProgress  { get; set; }
	public Action<bool> onComplete { get; set; }
	public string ERROR {get; private set;}

	// Percentage Helpers
	int secondsConverted, targetSeconds;
	public int progress {get; private set;} // Current progress % of the current conversion

	// Ogg vorbis Quality Number to kbps.
	public static readonly int[] QUALITY = { 64, 80, 96, 112, 128, 160, 192, 224, 256, 320, 500 };
	// -----------------------------------------

	/// <summary>
	/// FFMPEG wrapper
	/// </summary>
	/// <param name="executablePath">Set the path of ffmpeg if not on path already</param>
	public FFmpeg(string executablePath = "")
	{
		proc = new CliApp(Path.Combine(executablePath,EXECUTABLE_NAME));

		proc.onComplete = (code) =>
		{
			if (code == 0)
			{
				onComplete?.Invoke(true);
			}
			else
			{
				ERROR = "Something went wrong with FFMPEG";
				onComplete(false);
			}
		};


		// Get and calculate progress, "targetSeconds" needs to be set for this to work
		proc.onStdErr = (s) =>
		{
			if (targetSeconds == 0) return;
			secondsConverted = readSecondsFromOutput(s, @"time=(\d{2}):(\d{2}):(\d{2})");
			if (secondsConverted == -1) return;

			progress = (int)Math.Ceiling(((double)secondsConverted / (double)targetSeconds) * 100f);
			// LOG.log("[FFMPEG] : {0} / {1} = {2}", secondsConverted, targetSeconds, progress);

			if (progress > 100) progress = 100;
			onProgress?.Invoke(progress);
		};

	}// -----------------------------------------

	// --
	public void kill() => proc?.kill();

	/// <summary>
	/// Read a file's duration, used for when converting to PCM
	/// </summary>
	/// <param name="file"></param>
	private int getSecondsFromFile(string input)
	{
		int i = 0;
		var s = CliApp.quickStartSync(proc.executable, string.Format("-i \"{0}\" -f null -", input));
		if(s[2]=="0") // ffmpeg success 
		{
			i = readSecondsFromOutput(s[1], @"\s*Duration:\s*(\d{2}):(\d{2}):(\d{2})");
			LOG.log("[FFMPEG] : {0} duration in seconds = {1}", input, i);
		}
		return i;
	}// -----------------------------------------


	/// <summary>
	/// Returns FFMPEG time to seconds. HELPER FUNCTION
	/// </summary>
	/// <param name="input">The string to check the regex</param>
	/// <param name="expression">Needs to be a regexp with 3 capture groups</param>
	/// <returns></returns>
	private int readSecondsFromOutput(string input,string expression)
	{
		var m = Regex.Match(input,expression);
		var seconds = -1;
		if(m.Success){
			var hh = int.Parse(m.Groups[1].Value);
			var mm = int.Parse(m.Groups[2].Value);
			var ss = int.Parse(m.Groups[3].Value);
			seconds = (ss + (mm * 60) + (hh * 360));
		}
		return seconds;
	}// -----------------------------------------

	/// <summary>
	/// Convert an audio file to a PCM file for use in a CD audio
	/// ! Does not check INPUT file !
	/// ! Overwrites all generated files !
	/// </summary>
	/// <param name="input"></param>
	/// <param name="output">If ommited, will be automatically set</param>
	/// <returns></returns>
	public bool audioToPCM(string input,string output = null)
	{
		LOG.log("[FFMPEG] : Converting \"{0}\" to PCM",input);
		
		if(string.IsNullOrEmpty(output)) {
			output = Path.ChangeExtension(input,"pcm");
		}

		// Prepare progress variables
		secondsConverted = progress = 0;
		targetSeconds = getSecondsFromFile(input);

		proc.start(string.Format("-i \"{0}\" -y -f s16le -acodec pcm_s16le \"{1}\"", input, output));
		// note: If oncomplete is set, it will be called 
		//		 If onprogress is set, it will be called with progress 
		return true;
	}// -----------------------------------------

	/// <summary>
	/// Convert a PCM audio file to OGG
	/// ! Overwrites all generated files !
	/// ! Does not check INPUT file !
	/// </summary>
	/// <param name="input"></param>
	/// <param name="OGGquality">0(64kbps) to 10(500kbps)</param>
	/// <param name="output">If ommited, will be automatically set</param>
	/// <returns></returns>
	public bool audioPCMToOgg(string input,int OGGquality,string output = null)
	{
		// [safequard]
		if (OGGquality < 0) OGGquality = 0;
		else if (OGGquality > 10) OGGquality = 10;

		if(string.IsNullOrEmpty(output)) {
			output = Path.ChangeExtension(input,"ogg");
		}else{
			//[safeguard] Make sure it's an OGG
			if(!output.ToLower().EndsWith(".ogg")) {
				output += ".ogg"; // try to fix it
			}
		}

		LOG.log("[FFMPEG] : Converting \"{0}\" to OGG, Quality {1}",input,QUALITY[OGGquality]);

		_initProgressVars(input);

		proc.start(string.Format(
				"-y -f s16le -ar 44.1k -ac 2 -i \"{0}\" -c:a libvorbis -q {1} \"{2}\"",
				input,OGGquality,output
			));

		return true;
	}// -----------------------------------------

	/// <summary>
	/// Convert a PCM audio file to FLAC
	/// ! Overwrites all generated files !
	/// ! Does not check INPUT file !
	/// </summary>
	/// <param name="input"></param>
	/// <param name="output">If ommited, will be automatically set</param>
	/// <returns></returns>
	public bool audioPCMToFlac(string input,string output = null)
	{
		if(string.IsNullOrEmpty(output)) {
			output = Path.ChangeExtension(input,"flac");
		}else{
			//[safeguard] Make sure it's a FLAC
			if(!output.ToLower().EndsWith(".flac")) {
				output += ".flac"; // try to fix it
			}
		}

		LOG.log("[FFMPEG] : Converting \"{0}\" to FLAC",input);

		_initProgressVars(input);

		// C#6 string interpolation
		proc.start($"-y -f s16le -ar 44.1k -ac 2 -i \"{input}\" -c:a flac \"{output}\"");
		//proc.start(string.Format("-y -f s16le -ar 44.1k -ac 2 -i \"{0}\" -c:a flac \"{1}\"", input,output));

		return true;
	}// -----------------------------------------


	// Helper
	void _initProgressVars(string input)
	{
		var fsize = (int)new FileInfo(input).Length;
		secondsConverted = progress = 0;
		targetSeconds = (int)Math.Floor((double)fsize / 176400); // PCM is 176400 bytes per second
		// LOG.log("[FFMPEG] : FILE SIZE = {0}, TARGET SECONDS = {1}", fsize, targetSeconds);
	}// -----------------------------------------



}// -- end class

}// --
