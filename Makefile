CC=mcs

VERSION := $(shell grep "^\[assembly\:AssemblyVersionAttribute" Assembly.cs | awk -F\" '{ print $$2 }' - | awk -F\. '{printf "%s.%s", $$1, $$2; if ($$3 != '0' || $$4 != '0') printf ".%s", $$3; if ($$4 && $$4 != '0') printf ".%s", $$4}')
BUILD_ZIP_DIR=.build-zip
ZIP_DIR=forge-ahead-$(VERSION)

create-zipdir=mkdir -p "$(BUILD_ZIP_DIR)/$(ZIP_DIR)"
remove-zipdir=rm -rf "$(BUILD_ZIP_DIR)"

define copy-readme
	cp COPYING.txt "$(BUILD_ZIP_DIR)/$(ZIP_DIR)"
	cp README.md "$(BUILD_ZIP_DIR)/$(ZIP_DIR)"
endef

define copy-plugins
	mkdir -p "$(BUILD_ZIP_DIR)/$(ZIP_DIR)/Plugins"
	cp Plugins/README.txt "$(BUILD_ZIP_DIR)/$(ZIP_DIR)/Plugins/"
	cp Plugins/*.dll "$(BUILD_ZIP_DIR)/$(ZIP_DIR)/Plugins/"
	cp Plugins/Makefile "$(BUILD_ZIP_DIR)/$(ZIP_DIR)/Plugins/"
endef

all: .FORCE
	$(CC) @weland.rsp
	make -C Plugins
.FORCE:

plugins:
	make -C Plugins

windows: .FORCE
	$(CC) @windows.rsp
ForgeAhead.app: .FORCE
	$(CC) @weland.rsp
	rm -rf ForgeAhead.app
	mkdir -p ForgeAhead.app/Contents
	sed -e 's/WELAND_VERSION/$(VERSION)/g' mac/Info.plist > ForgeAhead.app/Contents/Info.plist
	mkdir -p ForgeAhead.app/Contents/Resources
	cp icons/Weland.icns ForgeAhead.app/Contents/Resources/
	mkdir -p ForgeAhead.app/Contents/MacOS
	cp mac/weland.sh ForgeAhead.app/Contents/MacOS/weland
	chmod u+x ForgeAhead.app/Contents/MacOS/weland
	cp ForgeAhead.exe ForgeAhead.app/Contents/MacOS/ForgeAhead.exe

winzip: windows plugins
	$(create-zipdir)
	$(copy-readme)
	$(copy-plugins)
	cp ForgeAhead.exe "$(BUILD_ZIP_DIR)/$(ZIP_DIR)"
	cd "$(BUILD_ZIP_DIR)" && zip -r "../forge-ahead-$(VERSION)-win.zip" "$(ZIP_DIR)"
	$(remove-zipdir)
maczip: ForgeAhead.app plugins
	$(create-zipdir)
	$(copy-readme)
	$(copy-plugins)
	cp -r ForgeAhead.app "$(BUILD_ZIP_DIR)/$(ZIP_DIR)"
	cd "$(BUILD_ZIP_DIR)" && zip -y -r "../forge-ahead-$(VERSION)-mac.zip" "$(ZIP_DIR)"
	$(remove-zipdir)
dist:
	$(create-zipdir)
	zip -r "$(BUILD_ZIP_DIR)/tmp.zip" . -i Makefile -i \*.cs -i \*.png -i \*.glade -i \*.txt -i \*.rsp -i mac/Info.plist -i mac/weland.sh -i icons/Weland.icns -i icons/Weland.ico -i Plugins/Makefile -i Plugins/README.txt
	unzip -d "$(BUILD_ZIP_DIR)/$(ZIP_DIR)" "$(BUILD_ZIP_DIR)/tmp.zip"
	cd "$(BUILD_ZIP_DIR)" && zip -r "../forge-ahead-$(VERSION)-src.zip" "$(ZIP_DIR)"
	$(remove-zipdir)
release: winzip maczip plugins dist
.PHONY: clean
clean:
	$(remove-zipdir)
	rm -f ForgeAhead.exe
	rm -rf ForgeAhead.app
	rm -rf *.zip
	rm -rf *.mdb
	make -C Plugins clean
