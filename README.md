# TwitchUnjail
Twitch vod downloader offering good speeds and low cpu utilization.

Application can either be controlled via command line arguments or by entering requested data while the app is running.

# Using command line arguments

Required arguments:
- `-vod URL` the vod url to download
- `--output PATH` or `-o PATH` the path to download to (excluding filename)

Optional arguments:
- `--quality QUALITY` or `-q QUALITY` the quality setting used for the download (see quality section below), will default to `source` quality if not used
- `--name NAME` or `-n NAME` the download file name to use, will default to an auto-generated name if not used

# Entering data while app is running

- Run the app
- It will ask to enter the vod url to download, enter/paste it and press enter
- It will query the available qualities (see quality section below), enter the desired quality and press enter
- It will ask for a download path (not filename), enter/paste it and press enter
- The app will now start downloading and print progress updates on screen

# Quality Settings

Settings that can be used in the app.
> Hint: `source` quality can be of any resolution or fps.

| Quality Setting | Short Form | Resolution | FPS   |
|-----------------|------------|------------|-------|
| AudioOnly       |            |            |       |
| 144p30          | 144p       |            | 30    |
| 160p30          | 160p       |            | 30    |
| 360p30          | 360p       |            | 30    |
| 360p60          |            |            | 60    |
| 480p30          | 480p       |            | 30    |
| 480p60          |            |            | 60    |
| 720p30          | 720p       | 1280x720   | 30    |
| 720p60          |            | 1280x720   | 60    |
| 1080p30         | 1080p      | 1920x1080  | 30    |
| 1080p60         |            | 1920x1080  | 60    |
| 1440p30         | 1440p      | 2560x1440  | 30    |
| 1440p60         |            | 2560x1440  | 60    |
| 4K30            | 4K30       |            | 30    |
| 4K60            |            |            | 60    |
| Source          |            |            | 30/60 |

# Run Examples

`TwitchUnfail-1.0-rc1.exe`

`TwitchUnfail-1.0-rc1.exe --vod https://www.twitch.tv/videos/11111111 --output C:\twitch`

`TwitchUnfail-1.0-rc1.exe --vod https://www.twitch.tv/videos/11111111 -q 720p -o C:\twitch`
