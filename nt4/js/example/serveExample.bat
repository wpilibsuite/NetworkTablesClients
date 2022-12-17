cd %~dp0\..
start "" http://localhost:5800/example/outlineViewer.html
python -m http.server 5800
pause