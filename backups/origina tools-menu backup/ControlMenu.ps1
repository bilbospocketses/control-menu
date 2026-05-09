# This script creates a menu system for some common tasks to help manage Android devices & Jellyfin

# It also has an icon creator function that calls another script from SpongySoft to create icon files from images

# Clear the screen

Clear-Host

# Check if the script is already running as Administrator and relaunches with elevation if not

if (!([Security.Principal.WindowsPrincipal] [Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole] "Administrator")) {
	# Relaunch the script with elevated privileges
	$scriptPath = $MyInvocation.MyCommand.Path
	Start-Process pwsh -ArgumentList "-NoProfile -ExecutionPolicy Bypass -File `"$scriptPath`"" -Verb RunAs
	exit
}

# Fill array with MAC address targets for Android devices

$macAddressArray = @("<REDACTED>" , "<REDACTED>" , "<REDACTED>") # Google TV Bedroom MAC, Google TV Living Room MAC, Pixel 9 MAC (hardware MACs redacted from archive)

# Initialize an index variable to count through array

$index = 0

# Loop through array to get IP addresses of Android devices and fill variables

# Ping each device once to bring it into the local ARP table

ping -n 1 <REDACTED> | Out-Null # LAN IP redacted from archive
ping -n 1 <REDACTED> | Out-Null # LAN IP redacted from archive
ping -n 1 <REDACTED> | Out-Null # LAN IP redacted from archive

do {
	# Get MAC address from array
	$macAddress = $macAddressArray[$index]
	# Get IP address of Android device
	$ipAddress = arp -a | Select-String -Pattern $macAddress
	# If IP address is found, fill variable with IP address
	if ($ipAddress) {
		# Get the IP address from the output
		$ipAddress = $ipAddress -split " " | Select-String -Pattern "\d{1,3}\.\d{1,3}\.\d{1,3}\.\d{1,3}"
		# Fill the variables with IPs
		switch ($index) {
			0 {
				$GoogleTVBR = $ipAddress
			}
			1 {
				$GoogleTVLR = $ipAddress
			}
			2 {
				$Pixel9 = $ipAddress
			}
		}
	}
	# Increment the index variable
	$index++
} while ($index -lt $macAddressArray.Count)

# Define variable for Jellyfin backup and log directory

$JellyfinFolder = "jellyfin-db-bkup-and-logs"

# Define the main menu function

function Show-Menu {
	do {
		$Host.UI.RawUI.BackgroundColor = 'Black'
		Clear-Host
		Write-Host "                           Tools Menu                           " -ForegroundColor White -BackgroundColor DarkBlue
		Write-Host "                           ==========                           " -ForegroundColor White -BackgroundColor DarkBlue
		Write-Host ""
		Write-Host " 1. Google TV Sub Menu" -ForegroundColor White
		Write-Host ""
		Write-Host " 2. Pixel 9 Sub Menu" -ForegroundColor White
		Write-Host ""
		Write-Host " 3. Update Jellyfin Database Media Date Settings" -ForegroundColor White
		Write-Host ""
		Write-Host " 4. Update Jellyfin Cast & Crew Images" -ForegroundColor White
		Write-Host ""
		Write-Host " 5. Create an ico File from an Image (jpg, png, etc)" -ForegroundColor White
		Write-Host ""
		Write-Host " 6. Unblock all files in a specific directory and all subdirectories" -ForegroundColor White
		Write-Host ""
		Write-Host " 0. Cleanup Temporary Configurations and Exit" -ForegroundColor White
		Write-Host ""
		
		# Get user input
		Write-Host "Enter your choice (0-6): " -NoNewline -ForegroundColor Green
		
		# Set default font color to white
		$Host.UI.RawUI.ForegroundColor = 'White'
		$choice = Read-Host 
		
		# Commands
		switch ($choice) {
			1 {
				# Go to Google TV Menu
				G
			}
			2 {
				# Go to Pixel 9 Menu
				P
			}
			3 {
				# Update Jellyfin database media date settings
				$Host.UI.RawUI.ForegroundColor = 'White'
				# Write Jellyfin container info to variable
				$content = docker ps --filter name=jellyfin
				# Line 2 is what we want from the output
				$lineNumber = 2
				# Number of characters from chosen line to read into $containerID variable
				$numChars = 12
				# Pull line 2 of $content into variable
				$desiredText = $content[$lineNumber - 1]
				# Fill $containerID with Jellyfin container number
				$containerID = $desiredText.Substring(0, $numChars)
				Clear-Host
				Write-Host ""
				Write-Host "This script updates the DateCreated field in the Jellyfin database to the date the show or movie was released." -ForegroundColor White
				Write-Host ""
				# Log script output
				Start-Transcript -Append $JellyfinFolder\JellyfinDBUpdateLog.txt
				# Stop docker container
				Write-Host ""
				Write-Host "Stopping docker container... " -ForegroundColor White -NoNewline
				docker stop -t=15 $containerID
				Write-Host ""
				# Create a variable with the current date and time
				$dateTime = Get-Date -Format "yyyyMMdd_HHmmss"
				# Copy the database file with the current date in the filename
				Write-Host "Creating database backup... " -ForegroundColor White -NoNewline
				Copy-Item -Path "D:\DockerData\jellyfin\config\data\library.db" -Destination "$JellyfinFolder\library_$dateTime.db"
				Write-Host "completed." -ForegroundColor White
				Write-Host ""
				# Run a SQL command on the database file
				Write-Host "Updating database... " -ForegroundColor White -NoNewline
				Invoke-SqliteQuery -Database "D:\DockerData\jellyfin\config\data\library.db" -Query "UPDATE TypedBaseItems SET DateCreated=PremiereDate;"
				Write-Host "completed." -ForegroundColor White
				Write-Host ""
				# Start docker container
				Write-Host "Starting docker container... " -ForegroundColor White -NoNewline
				docker start $containerID
				Write-Host ""
				# Remove backup files older than 5 days
				Get-ChildItem -Path "$JellyfinFolder" -Recurse | Where-Object { $_.LastWriteTime -lt (Get-Date).AddDays(-5) } | Remove-Item
				Stop-Transcript
				Write-Host ""
				Write-Host "Process completed. Backups older than 5 days have been deleted. Returning to main menu. " -ForegroundColor Green -NoNewline
				$Host.UI.RawUI.ForegroundColor = 'Green'
				Pause
			}
			4 {
				# Update Jellyfin Cast & Crew Images
				Write-Host ""
				Write-Host "Updating images for all cast members, directors and producers in Jellyfin media libraries." -ForegroundColor White
				Write-Host ""
				Write-Host "This process takes about a week to complete. A confirmation email will be sent once it finishes." -ForegroundColor White
				Write-Host ""
				Write-Host "The log file can be checked at C:\scripts\tools-menu\jellyfin-db-bkup-and-logs\jellyfin-cast-pics-update-log.txt." -ForegroundColor White
				Write-Host ""
				Write-Host "If the computer is restarted or the powershell.exe background process is terminated before the email is received this task did not complete successfully." -ForegroundColor White
				# Start the process with -WindowStyle Hidden
				Start-Process powershell -ArgumentList "-NoProfile -ExecutionPolicy Bypass -File Jellyfin-Cast-Update.ps1" -WindowStyle Hidden
				Write-Host ""
				$Host.UI.RawUI.ForegroundColor = 'Green'
				Write-Host "Returning to main menu. " -ForegroundColor Green -NoNewline
				Pause
			}
			5 {
				# Create an ico File from an Image (jpg, png, etc)
				Write-Host ""
				Write-Host "Path to source image file, including name and extension (i.e. c:\mypics\somepic.png): " -ForegroundColor Green -NoNewline
				# Set default font color to white
				$Host.UI.RawUI.ForegroundColor = 'White'
				$SourcePath = Read-Host
				Write-Host ""
				Write-Host "Path to destination icon file, including name and extension (i.e. c:\myicons\program.ico): " -ForegroundColor Green -NoNewline
				$TargetPath = Read-Host
				Write-Host ""
				ConvertTo-Ico.ps1 -SourcePath $SourcePath -TargetPath $TargetPath -Formats 64,128,256
				Write-Host ""
				# Set default font color to green
				$Host.UI.RawUI.ForegroundColor = 'Green'
				Write-Host "Icon created in $TargetPath. Returning to main menu. " -ForegroundColor Green -NoNewline
				Pause
			}
			6 {
				$selectedPath = $null
				# Load the Windows Forms assembly
				Add-Type -AssemblyName System.Windows.Forms
				
				# Create a new instance of the FolderBrowserDialog
				$folderBrowser = New-Object System.Windows.Forms.FolderBrowserDialog
				
				# Show the dialog and check if the user clicked OK
				
				if ($folderBrowser.ShowDialog() -eq [System.Windows.Forms.DialogResult]::OK) {
					# Get the selected folder path
					$selectedPath = $folderBrowser.SelectedPath
					Write-Host ""
					Write-Host "You selected the folder: $selectedPath" -ForegroundColor White
					Write-Host ""					
				} else {
					Write-Host ""
					Write-Host "No folder was selected." -ForegroundColor White
					Write-Host ""
				}
				if ($selectedPath) {
					Write-Host "Unblocking all files in the selected directory and all subdirectories..." -ForegroundColor White -NoNewline
					Get-ChildItem $selectedPath -Recurse | Unblock-File
					Write-Host "completed. " -ForegroundColor White
					$Host.UI.RawUI.ForegroundColor = 'Green'
					Write-Host ""
					Write-Host "Returning to main menu. " -ForegroundColor Green -NoNewline
					Pause
				}
				else {
					Write-Host "Returning to main menu. " -ForegroundColor Green -NoNewline
					$Host.UI.RawUI.ForegroundColor = 'Green'
					Pause
				}
			}
			0 {
				# Cleanup temporary configurations and exit
				Clear-Host
				Write-Host "                            Exiting                             " -ForegroundColor White -BackgroundColor DarkBlue
				Write-Host "                            =======                             " -ForegroundColor White -BackgroundColor DarkBlue -NoNewline
				Write-Host "                                                                                                                                      " -BackgroundColor Black
				# Set default output font color to white
				$Host.UI.RawUI.ForegroundColor = 'White'
				Write-Host "Disconnecting current adb sessions: "
				Write-Host ""
				# Get all connected adb devices into array
				$adbOutput = adb devices
				# Remnove first line from output, as it always contains a header, and fill another array with the rest
				$adbDevices = @($adbOutput | Select-Object -Skip 1)
				# Create an empty array to hold IP addresses
				$ipAddressArray = @()
				# Loop through the $adbDevices array to get the IP addresses of the devices and fill the $ipAddresses Array
				foreach ($adbDevice in $adbDevices) {
					if ($adbDevice -match '\d{1,3}\.\d{1,3}\.\d{1,3}\.\d{1,3}') {
						$ipAddressArray += $Matches[0]
					}
				}
				if ($ipAddressArray) {
					foreach ($ipAddress in $ipAddressArray) {
						if ($ipAddress -eq $GoogleTVLR) {
							Write-Host "Google TV Living Room" -ForegroundColor White -NoNewline
							adb disconnect $GoogleTVLR | Out-Null
							Write-Host " disconnected..." -ForegroundColor DarkRed
						}
						elseif ($ipAddress -eq $GoogleTVBR) {
							Write-Host "Google TV Bedroom" -ForegroundColor White -NoNewline
							adb disconnect $GoogleTVBR | Out-Null
							Write-Host " disconnected..." -ForegroundColor DarkRed
						}
						elseif ($ipAddress -eq $Pixel9) {
							Write-Host "Pixel 9" -ForegroundColor White -NoNewline
							adb disconnect $Pixel9 | Out-Null
							Write-Host " disconnected..." -ForegroundColor DarkRed
						}
					}
				}
				else {
					Write-Host "No devices connected." -ForegroundColor White
				}
				Write-Host ""
				# Set default output font color to green
				$Host.UI.RawUI.ForegroundColor = 'Green'
				Write-Host "Resetting terminal and exiting. " -NoNewline
				Pause
				# Set default output font color back to gray
				$Host.UI.RawUI.ForegroundColor = 'Gray'
				Clear-Host
				Exit
			}
			default {
				Write-Host ""
				Write-Host "Invalid choice. Please select a valid option, 0 through 6. " -ForegroundColor White -BackgroundColor DarkRed -NoNewline
				# Set default output font color to White
				$Host.UI.RawUI.ForegroundColor = 'White'
				# Set default background color to dark red
				$Host.UI.RawUI.BackgroundColor = 'DarkRed'
				Pause
			}
		}
	} while ($choice -ne 0)
}

# Define the Google TV menu function

function G {
	# Connect to Google TV Living Room or Bedroom
	# Set default font color to white
	adb disconnect $GoogleTV | Out-Null
	Clear-Host
	Write-Host "                         Google TV Choice                         " -ForegroundColor White -BackgroundColor DarkBlue
	Write-Host "                         ================                         " -ForegroundColor White -BackgroundColor DarkBlue
	$Host.UI.RawUI.ForegroundColor = 'White'
	Write-Host ""
	Write-Host "1. Living Room" -ForegroundColor White
	Write-Host ""
	Write-Host "2. Bedroom" -ForegroundColor White
	Write-Host ""
	Write-Host "0. Return to Main Menu" -ForegroundColor White
	Write-Host ""
	Write-Host "Connect to a Google TV or return to Main Menu: " -ForegroundColor Green -NoNewline 
	# Set default font color to white
	$Host.UI.RawUI.ForegroundColor = 'White'
	do {
		$GoogleTVChoice = Read-Host
		switch ($GoogleTVChoice) {
			1 {
				$GoogleTV = $GoogleTVLR
				adb connect $GoogleTV | Out-Null
			}
			2 {
				$GoogleTV = $GoogleTVBR
				adb connect $GoogleTV | Out-Null
			}
			0 {
				# Return to main menu
				Show-Menu
			}
			default {
				Write-Host ""
				Write-Host "Invalid choice. Please select a valid option (0-2)" -ForegroundColor DarkRed
				Write-Host ""
				Write-Host "Connect to a Google TV or return to Main Menu: " -ForegroundColor Green -NoNewline
			}
		}
	} while (($GoogleTVChoice -ne '1') -and ($GoogleTVChoice -ne '2') -and ($GoogleTVChoice -ne '0'))

	# Set default font color to green
	$Host.UI.RawUI.ForegroundColor = 'Green'

	do {
		$Host.UI.RawUI.BackgroundColor = 'Black'
		Clear-Host
		Write-Host "                         Google TV Menu                         " -ForegroundColor White -BackgroundColor DarkBlue
		Write-Host "                         ==============                         " -ForegroundColor White -BackgroundColor DarkBlue
		Write-Host "                                                                " -ForegroundColor White -BackgroundColor DarkBlue
		if ($GoogleTVChoice -eq '1') {
			Write-Host "                   Connected to Living Room TV                  " -ForegroundColor White -BackgroundColor DarkBlue
			Write-Host "                   ---------------------------                  " -ForegroundColor White -BackgroundColor DarkBlue
			Write-Host ""
		}
		elseif ($GoogleTVChoice -eq '2') {
			Write-Host "                     Connected to Bedroom TV                    " -ForegroundColor White -BackgroundColor DarkBlue
			Write-Host "                     -----------------------                    " -ForegroundColor White -BackgroundColor DarkBlue
			Write-Host ""
		}
		Write-Host " 1. Power Status" -ForegroundColor White
		Write-Host ""
		Write-Host " 2. Reboot" -ForegroundColor White
		Write-Host ""
		Write-Host " 3. Power Off/On" -ForegroundColor White
		Write-Host ""
		Write-Host " 4. Screen Mirroring" -ForegroundColor White
		Write-Host ""
		Write-Host " 5. Restore Projectivy Settings from Backup" -ForegroundColor White
		Write-Host ""
		Write-Host " 6. Start Shizuku" -ForegroundColor White
		Write-Host ""
		Write-Host " 7. Screen Saver Settings" -ForegroundColor White
		Write-Host ""
		Write-Host " 8. Enable/Disable Android TV Launcher" -ForegroundColor White
		Write-Host ""
		Write-Host " 0. Return to Main Menu" -ForegroundColor White
		Write-Host ""
		
		# Get user input
		Write-Host "Enter your choice (0-8): " -ForegroundColor Green -NoNewline
		
		# Set default font color to white
		$Host.UI.RawUI.ForegroundColor = 'White'
		$choice = Read-Host
		
		# Commands
		switch ($choice) {
			1 {
				# Google TV Power Status
				# Set default font color to white
				$Host.UI.RawUI.ForegroundColor = 'White'
				Write-Host ""
				$GoogleTVPowerStatus = adb -s $GoogleTV shell dumpsys power
				if ($GoogleTVPowerStatus | Select-String -Pattern "mwakefulness=awake") {
					# Set default font color to green
					$Host.UI.RawUI.ForegroundColor = 'Green'
					Write-Host "Google TV is currently powered on. " -ForegroundColor Green -NoNewline
					Write-Host "Returning to Google TV menu. " -ForegroundColor Green -NoNewline
					Pause
				}
				else {
					# Set default font color to green
					$Host.UI.RawUI.ForegroundColor = 'Green'
					Write-Host "Google TV is currently powered off. " -ForegroundColor Green -NoNewline
					Write-Host "Returning to Google TV menu. " -ForegroundColor Green -NoNewline
					Pause
				}
			}
			2 {
				# Reboot Google TV
				# Set default font color to white
				$Host.UI.RawUI.ForegroundColor = 'White'
				Write-Host ""
				Write-Host "Rebooting Google TV. Please wait..." -ForegroundColor White
				Write-Host ""
				adb -s $GoogleTV shell reboot
				# Set default font color to green
				$Host.UI.RawUI.ForegroundColor = 'Green'
				do {
					Start-Sleep -Seconds 10
					$GoogleTVPing = ping -n 1 $GoogleTV
				} while (!($GoogleTVPing | Select-String -Pattern "Reply from ${GoogleTV}: bytes=32" -SimpleMatch))
				Write-Host "Google TV is online after reboot. Returning to Google TV menu. " -ForegroundColor Green -NoNewline
				Pause
			}
			3 {
				# Power Google TV Off/On
				# Set default font color to white
				$Host.UI.RawUI.ForegroundColor = 'White'
				Write-Host ""
				$GoogleTVPowerStatus = adb -s $GoogleTV shell dumpsys power
				# Set default font color to green
				$Host.UI.RawUI.ForegroundColor = 'Green'
				if ($GoogleTVPowerStatus | Select-String -Pattern "mwakefulness=awake") {
					do {
						Write-Host "Google TV is currently powered on. Power off (Y/N)? " -ForegroundColor Green -NoNewline
						$Host.UI.RawUI.ForegroundColor = 'White'
						$PowerChoice = Read-Host
						switch ($PowerChoice) {
							Y {
								adb -s $GoogleTV shell input keyevent KEYCODE_POWER
								Write-Host ""
								Write-Host "Google TV powered off. Returning to Google TV menu. " -ForegroundColor Green -NoNewline
							}
							N {
								Write-Host ""
								Write-Host "No actions performed, exiting. " -ForegroundColor Green -NoNewline
							}
							default {
								Write-Host ""
								Write-Host "Invalid choice. Please select a valid option (Y/N)" -ForegroundColor DarkRed
								Write-Host ""
							}
						}
					} while (($PowerChoice -ine 'Y') -and ($PowerChoice -ine 'N'))
				}
				else {
					do {
						Write-Host "Google TV is currently powered off. Power on (Y/N)? " -ForegroundColor Green -NoNewline
						$Host.UI.RawUI.ForegroundColor = 'White'
						$PowerChoice = Read-Host
						switch ($PowerChoice) {
							Y {
								adb -s $GoogleTV shell input keyevent KEYCODE_POWER
								Write-Host ""
								Write-Host "Google TV powered on. Returning to Google TV menu. " -ForegroundColor Green -NoNewline
							}
							N {
								Write-Host ""
								Write-Host "No actions performed, exiting. " -ForegroundColor Green -NoNewline
							}
							default {
								Write-Host ""
								Write-Host "Invalid choice. Please select a valid option (Y/N)" -ForegroundColor DarkRed
								Write-Host ""
							}
						}
					} while (($PowerChoice -ine 'Y') -and ($PowerChoice -ine 'N'))
				}
				# Set default font color to green
				$Host.UI.RawUI.ForegroundColor = 'Green'
				Pause
			}
			4 {
				# Google TV Screen Mirroring
				# Set default font color to white
				$Host.UI.RawUI.ForegroundColor = 'White'
				Write-Host ""
				Write-Host "Remoting to Google TV. Close the remote screen once finished to return to the Google TV menu." -ForegroundColor White
				Write-Host ""
				Start-Sleep -Seconds 5
				scrcpy --video-encoder=OMX.google.h264.encoder --no-audio -s $GoogleTV | Out-Null
			}
			5 {
				# Restore Projectivy Settings to Google TV from Backup
				Write-Host ""
				# Define the directory path on the Android device
				$directoryPath = "/storage/emulated/0/Projectivy-Backups"
				# Run the adb shell command to list the folder contents
				$fileList = adb -s $GoogleTV shell ls $directoryPath
				# Check if the adb command returned any output
				if ($fileList) {
					# Split the output into an array of file names (split by newline)
					$fileArray = $fileList -split "`n"
					$fileArray += "Cancel - Return to Google TV menu"
					# Variable to store the user's selection
					$selectedOption = $null
					# Do-While loop to display the list and prompt the user until a valid selection is made
					do {
						$Host.UI.RawUI.BackgroundColor = 'Black'
						#Clear-Host
						Clear-Host
						Write-Host "Projectivy Launcher settings files available for restore:" -ForegroundColor White
						Write-Host ""
						# Display the numbered list of files
						for ($i = 0; $i -lt $fileArray.Count; $i++) {
							# Set default font color to white with dark blue background
							$Host.UI.RawUI.ForegroundColor = 'DarkBlue'
							$Host.UI.RawUI.BackgroundColor = 'Black'
							Write-Host "     $($i + 1). $($fileArray[$i])"
							Write-Host ""
						}
						$Host.UI.RawUI.BackgroundColor = 'Black'
						Write-Host "Please select the corresponding number of the file to restore Projectivy Launcher settings: " -ForegroundColor Green -NoNewline
						# Set default font color to white
						$Host.UI.RawUI.ForegroundColor = 'White'
						# Prompt the user for input
						$userInput = Read-Host
						# Validate the input
						if ([int]::TryParse($userInput, [ref]$selectedOption) -and $selectedOption -ge 1 -and $selectedOption -le $fileArray.Count) {
							# Valid input: break the loop
							break
						}
						else {
							# Invalid input: display an error message
							Write-Host ""
							Write-Host "Invalid selection. Please enter a number between 1 and $($fileArray.Count). " -ForegroundColor White -BackgroundColor DarkRed -NoNewline
							# Set default font color to white with dark red background
							$Host.UI.RawUI.ForegroundColor = 'White'
							$Host.UI.RawUI.BackgroundColor = 'DarkRed'
							Pause
						}
					} while ($true)
					# Get the selected file based on the user's input
					$selectedFile = $fileArray[$selectedOption - 1]
					# Decision to pass parameters to Projectivy Launcher to restore settings or exit
					if (!($selectedFile | Select-String -Pattern "Cancel - Return to Google TV menu")) {
						adb -s $GoogleTV shell am start -a android.intent.action.VIEW -d "file://$directoryPath/$selectedFile" -n com.spocky.projengmenu/.ui.launcherActivities.ImportSettingsActivity | Out-Null
						Write-Host ""
						Write-Host "File selected: $selectedFile"
						Write-Host ""
						Write-Host "Remoting to Google TV for the confirmation dialog. Close the remote screen to return to the Google TV menu." -ForegroundColor White
						Write-Host ""
						Start-Sleep -Seconds 5
						scrcpy --video-encoder=OMX.google.h264.encoder --no-audio -s $GoogleTV | Out-Null
					}
					elseif ($selectedFile = "Cancel - Return to Google TV menu") {
						$Host.UI.RawUI.ForegroundColor = 'Green'
						Write-Host ""
						Write-Host "Cancelling restore of Projectivy Launcher settings. Returning to Google TV menu. " -ForegroundColor Green -NoNewline
						Pause
					}
				}
				else {
					$Host.UI.RawUI.ForegroundColor = 'Green'
					Write-Host "No files found or the directory does not exist. Returning to Google TV menu. " -ForegroundColor Green -NoNewline
					Pause
				}
			}
			6 {
				# Start Shizuku on Google TV
				Write-Host ""
				Write-Host "Starting Shizuku..." -ForegroundColor White
				Write-Host ""
				adb -s $GoogleTV shell sh /storage/emulated/0/Android/data/moe.shizuku.privileged.api/start.sh | Out-Null
				$Host.UI.RawUI.ForegroundColor = 'Green'
				Write-Host "Shizuku started. Returning to Google TV menu. " -ForegroundColor Green -NoNewline
				Pause
			}
			7 {
				# Screen Saver Settings on Google TV
				# Set default font color to white
				$Host.UI.RawUI.ForegroundColor = 'White'
				Write-Host ""
				$GoogleTVScreenSaver = adb shell settings get secure screensaver_components
				if ($GoogleTVScreenSaver | Select-String -Pattern "com.snapwood.skyfolio/com.snapwood.skyfolio.DreamService") {
					do {
						Write-Host "Current screensaver is SkyFolio. Change to Google (Y/N)? " -ForegroundColor Green -NoNewline
						$Screensaver = Read-Host
						switch ($Screensaver) {
							Y {
								adb -s $GoogleTV shell settings put secure screensaver_components com.google.android.apps.tv.dreamx/.service.Backdrop
								Write-Host ""
								Write-Host "Google screensaver set as default." -ForegroundColor White
								Write-Host ""
							}
							N {
								Write-Host ""
								Write-Host "No actions performed." -ForegroundColor White
								Write-Host ""
							}
							default {
								Write-Host ""
								Write-Host "Invalid choice. Please select a valid option (Y/N)" -ForegroundColor DarkRed
								Write-Host ""
							}
						}
					} while (($Screensaver -ine 'Y') -and ($Screensaver -ine 'N'))
				}
				else {
					do {
						Write-Host "Current screensaver is Google. Change to SkyFolio (Y/N)? " -ForegroundColor Green -NoNewline
						# Set default font color to white
						$Host.UI.RawUI.ForegroundColor = 'White'
						$Screensaver = Read-Host
						switch ($Screensaver) {
							Y {
								adb -s $GoogleTV shell settings put secure screensaver_components com.snapwood.skyfolio/com.snapwood.skyfolio.DreamService
								Write-Host ""
								Write-Host "SkyFolio screensaver set as default." -ForegroundColor White
								Write-Host ""
							}
							N {
								Write-Host ""
								Write-Host "No actions performed." -ForegroundColor White
								Write-Host ""
							}
							default {
								Write-Host ""
								Write-Host "Invalid choice. Please select a valid option (Y/N)" -ForegroundColor DarkRed
								Write-Host ""
							}
						}
					} while (($Screensaver -ine 'Y') -and ($Screensaver -ine 'N'))
				}
				$ScreenTimeout = adb shell settings get system screen_off_timeout
				do {
					Write-Host "Current screensaver timeout is $ScreenTimeout milliseconds. Would you like to change it (Y/N)? " -ForegroundColor Green -NoNewline
					$Answer = Read-Host
					Write-Host ""
					# Set default font color to white
					$Host.UI.RawUI.ForegroundColor = 'White'
					switch ($Answer) {
						Y {
							do {
							Write-Host "Enter new screensaver timeout in milliseconds. Minimum allowable timeout is 300000 (5 minutes): " -ForegroundColor Green -NoNewline
							$Timeout = Read-Host
							# Set default font color to green
							$Host.UI.RawUI.ForegroundColor = 'Green'
								# Check if the input is a valid number
								if (-not ($Timeout -as [int])) {
									Write-Host ""
									Write-Host "Invalid input. Please enter a valid number of at least 300000." -ForegroundColor DarkRed
									Write-Host ""
									continue
								}
								# Check if the number is greater than the minimum value
								if ([int]$Timeout -le 299999) {
									Write-Host ""
									Write-Host "The number must be greater than 299999. Please try again." -ForegroundColor DarkRed
									Write-Host ""
									continue
								}
								
								# If both conditions are met, exit the loop
								break
							
							} While ($true)
							adb -s $GoogleTV shell settings put system screen_off_timeout $Timeout
							Write-Host ""
							Write-Host "Screensaver timeout changed to $Timeout milliseconds. Returning to Google TV menu. " -ForegroundColor Green -NoNewline
							Pause
						}
						N {
							# Set default font color to green
							$Host.UI.RawUI.ForegroundColor = 'Green'
							Write-Host "No actions performed. Returning to Google TV menu. " -ForegroundColor Green -NoNewline
							Pause
						}
						default {
							Write-Host "Invalid choice. Please select a valid option (Y/N)" -ForegroundColor DarkRed
							Write-Host ""
						}
					}
				} while (($Answer -ine 'Y') -and ($Answer -ine 'N'))
			}
			8 {
				# Enable/Disable Android TV Launcher
				Write-Host ""
				$GoogleTVPackages = adb shell pm list packages -d
				if ($GoogleTVPackages | Select-String -Pattern "com.google.android.apps.tv.launcherx") {
					do {
						Write-Host "Android TV Launcher is currently disabled. Enable (Y/N)? " -ForegroundColor Green -NoNewline
						$Host.UI.RawUI.ForegroundColor = 'White'
						$LauncherChoice = Read-Host
						switch ($LauncherChoice) {
							Y {
								adb shell pm enable com.google.android.apps.tv.launcherx | Out-Null
								adb shell pm enable com.google.android.tungsten.setupwraith | Out-Null
								Write-Host ""
								Write-Host "Android TV Launcher is enabled. " -ForegroundColor Green -NoNewline
							}
							N {
								Write-Host ""
								Write-Host "No actions performed, exiting. " -ForegroundColor Green -NoNewline
							}
							default {
								Write-Host ""
								Write-Host "Invalid choice. Please select a valid option (Y/N)" -ForegroundColor DarkRed
								Write-Host ""
							}
						}
					} while (($LauncherChoice -ine 'Y') -and ($LauncherChoice -ine 'N'))
				}
				else {
					do {
						Write-Host "Android TV Launcher is currently enabled. Disable (Y/N)? " -ForegroundColor Green -NoNewline
						$Host.UI.RawUI.ForegroundColor = 'White'
						$LauncherChoice = Read-Host
						switch ($LauncherChoice) {
							Y {
								adb shell pm disable-user --user 0 com.google.android.apps.tv.launcherx | Out-Null
								adb shell pm disable-user --user 0 com.google.android.tungsten.setupwraith | Out-Null
								Write-Host ""
								Write-Host "Android TV Launcher is disabled. " -ForegroundColor Green -NoNewline
							}
							N {
								Write-Host ""
								Write-Host "No actions performed, exiting. " -ForegroundColor Green -NoNewline
							}
							default {
								Write-Host ""
								Write-Host "Invalid choice. Please select a valid option (Y/N)" -ForegroundColor DarkRed
								Write-Host ""
							}
						}
					} while (($LauncherChoice -ine 'Y') -and ($LauncherChoice -ine 'N'))
				}
				# Set default font color to green
				$Host.UI.RawUI.ForegroundColor = 'Green'
				Pause
			}
			0 {
				# Return to main manu
				Show-Menu
			}
			default {
				Write-Host ""
				Write-Host "Invalid choice. Please select a valid option, 0 through 8. " -ForegroundColor White -BackgroundColor DarkRed -NoNewline
				# Set default output font color to White
				$Host.UI.RawUI.ForegroundColor = 'White'
				# Set default background color to dark red
				$Host.UI.RawUI.BackgroundColor = 'DarkRed'
				Pause
				G
			}
		}
	} while ($choice -ne 0)
}

# Define the Pixel 9 menu function

function P {
	do {
		$Host.UI.RawUI.BackgroundColor = 'Black'
		Clear-Host
		Write-Host "                          Pixel 9 Menu                          " -ForegroundColor White -BackgroundColor DarkBlue
		Write-Host "                          ============                          " -ForegroundColor White -BackgroundColor DarkBlue
		Write-Host ""
		Write-Host " 1. Reset adb Port to Default (5555) After Reboot" -ForegroundColor White
		Write-Host ""
		Write-Host " 2. Connect (adb)" -ForegroundColor White
		Write-Host ""
		Write-Host " 3. Screen Mirroring" -ForegroundColor White
		Write-Host ""
		Write-Host " 0. Return to Main Menu" -ForegroundColor White
		Write-Host ""
		
		# Get user input
		Write-Host "Enter your choice (0-3): " -ForegroundColor Green -NoNewline
		
		# Set default font color to white
		$Host.UI.RawUI.ForegroundColor = 'White'
		$choice = Read-Host
		
		# Commands
		switch ($choice) {
			1 {
				# Reset adb Port to Default (5555) on Pixel 9 After Reboot
				# Set default font color to white
				$Host.UI.RawUI.ForegroundColor = 'White'
				Clear-Host
				Write-Host "Cleaning up old adb sessions... " -ForegroundColor White -NoNewline
				adb disconnect
				Write-Host ""
				# Set default font color to green
				$Host.UI.RawUI.ForegroundColor = 'Green'
				Write-Host "Connect Pixel 9 to PC via USB C cable. " -ForegroundColor Green -NoNewline
				Pause
				Write-Host ""
				$adbDevices = adb devices
				if (!($adbDevices | Select-String -Pattern "<REDACTED>")) { # Pixel 9 hardware serial redacted from archive
					Write-Host "Pixel 9 not connected to USB. Returning to Pixel 9 menu. " -ForegroundColor Green -NoNewline
					Pause
					Write-Host ""
				}
				else {
					# Set default font color to white
					$Host.UI.RawUI.ForegroundColor = 'White'
					Write-Host "Phone is " -ForegroundColor White -NoNewline
					adb tcpip 5555
					Write-Host ""
					Write-Host "Reset ADP port on Pixel 9 to default (5555)."
					Write-Host ""
					Write-Host "Returning to Pixel 9 menu. " -ForegroundColor Green -NoNewline
					# Set default font color to green
					$Host.UI.RawUI.ForegroundColor = 'Green'
					Pause
				}
			}
			2 {
				# Connect to Pixel 9 (adb)
				# Set default font color to white
				$Host.UI.RawUI.ForegroundColor = 'White'
				Write-Host ""
				Write-Host "Connecting to Pixel 9..." -ForegroundColor White -NoNewline
				adb connect $Pixel9
				Write-Host ""
				# Set default font color to green
				$Host.UI.RawUI.ForegroundColor = 'Green'
				Write-Host "Returning to Pixel 9 menu. " -ForegroundColor Green -NoNewline
				Pause
			}
			3 {
				# Pixel 9 Screen Mirroring
				# Set default font color to white
				$Host.UI.RawUI.ForegroundColor = 'White'
				Write-Host ""
				Write-Host "Connecting to Pixel 9: " -ForegroundColor White -NoNewline
				adb connect $Pixel9
				Write-Host ""
				Write-Host "Remoting to Google Pixel 9. Close the remote screen once finished to return to the Pixel 9 menu." -ForegroundColor White
				Write-Host ""
				Start-Sleep -Seconds 5
				adb -s $Pixel9 shell input keyevent 26 # turns the screen on
				adb -s $Pixel9 shell input keyevent 82 # unlocks and asks for pin
				adb -s $Pixel9 shell input text <REDACTED> && adb -s $Pixel9 shell input keyevent 66 # types passcode (PIN redacted from archive) and presses enter
				scrcpy --video-encoder=OMX.google.h264.encoder --no-audio -s $Pixel9 | Out-Null
			}
			0 {
				# Return to main manu
				Show-Menu
			}
			default {
				Write-Host ""
				Write-Host "Invalid choice. Please select a valid option, 0 through 3. " -ForegroundColor White -BackgroundColor DarkRed -NoNewline
				# Set default output font color to White
				$Host.UI.RawUI.ForegroundColor = 'White'
				# Set default background color to dark red
				$Host.UI.RawUI.BackgroundColor = 'DarkRed'
				Pause
				P
			}
		}
	} while ($choice -ne 0)
}

# Start main menu
Show-Menu