Subject: New `releaseDate` Field Added to `subtitles.json` for Display on Web App

  Hi,

  I've updated the data pipeline to automatically include the original YouTube upload
  date for each video. This new piece of information is now available in the
  website/subtitles.json manifest file that the web app consumes.

  What's new:

  A new field, releaseDate, has been added to each video object in the JSON file.

   * Field Name: releaseDate
   * Data Format: The date is a string in YYYYMMDD format (e.g., "20231026").
   * Availability: This field will be present for all newly processed videos. For older
     videos that were processed before this change, the field may be null or absent
     entirely. The frontend code should handle this possibility gracefully.

  Example:

  Here is a comparison of a video entry in subtitles.json before and after the change:


  Before:

   1 {
   2   "v": "isIm67yGPzo",
   3   "title": "Two-Hatchery Against Mech Terran - 'Master Mech Strategies'",
   4   "description": "(no description)",
   5   "creator": "",
   6   "subtitleUrl": "https://pastebin.com/raw/Miy3QqBn",
   7   "tags": ["z", "zvt"]
   8 }

  After:

   1 {
   2   "v": "isIm67yGPzo",
   3   "title": "Two-Hatchery Against Mech Terran - 'Master Mech Strategies'",
   4   "description": "(no description)",
   5   "creator": "",
   6   "subtitleUrl": "https://pastebin.com/raw/Miy3QqBn",
   7   "releaseDate": "20231026",
   8   "tags": ["z", "zvt"]
   9 }

  Action for the Web App:

  Could you please update the user interface to display this releaseDate? It could be
  shown next to the video title or creator name. A great enhancement would also be to
  add a feature allowing users to sort the video list by this new date field.

  Let me know if you have any questions.