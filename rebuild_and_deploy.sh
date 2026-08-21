#!/bin/bash
dotnet publish src/NtmcScheduler.Web -c Release -r linux-x64 --self-contained false -o /tmp/ntmsy-schedule-publish
sudo systemctl stop ntmsy-schedule
sudo find /opt/ntmsy-schedule -mindepth 1 -maxdepth 1 -exec rm -rf -- {} +
sudo cp -a /tmp/ntmsy-schedule-publish/. /opt/ntmsy-schedule/
sudo chown -R root:root /opt/ntmsy-schedule
sudo find /opt/ntmsy-schedule -type d -exec chmod 755 {} +
sudo find /opt/ntmsy-schedule -type f -exec chmod 644 {} +
sudo chmod 755 /opt/ntmsy-schedule/NtmcScheduler.Web
sudo systemctl start ntmsy-schedule