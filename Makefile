.PHONY: help all clean restore build build-library build-collector build-dashboard deploy-collector test format lint watch publish-collector

CONFIG ?= Debug
RIDS ?= win-x64 linux-x64 osx-arm64 osx-x64
DASHBOARD_DIR := RimObs.Dashboard
SLN := RimObs.sln

# The collector must NOT live under Assemblies/: RimWorld's ModAssemblyHandler loads every
# .dll under Assemblies/ recursively and Mono segfaults trying to read the net10 collector's
# custom attributes. It deploys to <mod>/Collector/<rid>/ instead (CI ships all RIDs).
ifeq ($(OS),Windows_NT)
DEV_RID ?= win-x64
else
UNAME_S := $(shell uname -s)
UNAME_M := $(shell uname -m)
ifeq ($(UNAME_S),Darwin)
ifeq ($(UNAME_M),arm64)
DEV_RID ?= osx-arm64
else
DEV_RID ?= osx-x64
endif
else
DEV_RID ?= linux-x64
endif
endif
COLLECTOR_DEPLOY_DIR := Collector/$(DEV_RID)

help:
	@echo "Targets:"
	@echo "  all                 restore, build dashboard, build solution"
	@echo "  clean               remove bin/, obj/, Assemblies/, dashboard dist/"
	@echo "  restore             dotnet restore + pnpm install"
	@echo "  build               build the whole solution"
	@echo "  build-library       build only RimObs.Library"
	@echo "  build-collector     build only RimObs.Collector"
	@echo "  build-dashboard     build the Svelte SPA into dist/"
	@echo "  deploy-collector    framework-dependent host publish into Collector/<rid> for in-game auto-launch"
	@echo "  test                run xUnit suites"
	@echo "  format              dotnet format + prettier"
	@echo "  lint                dotnet format --verify-no-changes + eslint"
	@echo "  watch               dotnet watch on the collector"
	@echo "  publish-collector   self-contained publish for all RIDs"

all: restore build-dashboard build

clean:
	rm -rf Assemblies/*.dll Assemblies/*.pdb Assemblies/Collector Collector
	rm -rf RimObs.Library/bin RimObs.Library/obj
	rm -rf tests/RimObs.Library.Tests/bin tests/RimObs.Library.Tests/obj
	rm -rf RimObs.Wire/bin RimObs.Wire/obj
	rm -rf RimObs.Collector/bin RimObs.Collector/obj
	rm -rf tests/RimObs.Collector.Tests/bin tests/RimObs.Collector.Tests/obj
	rm -rf Source/RimObs.Patches.Harmony/bin Source/RimObs.Patches.Harmony/obj Harmony/Assemblies
	rm -rf Source/RimObs.Patches.Concord/bin Source/RimObs.Patches.Concord/obj Concord/Assemblies
	rm -rf tests/RimObs.Patches.Concord.Tests/bin tests/RimObs.Patches.Concord.Tests/obj
	rm -rf $(DASHBOARD_DIR)/dist

restore:
	dotnet restore $(SLN)
	cd $(DASHBOARD_DIR) && pnpm install --frozen-lockfile || pnpm install

build: build-dashboard
	dotnet build $(SLN) -c $(CONFIG) --nologo
	$(MAKE) deploy-collector CONFIG=$(CONFIG)

build-library:
	dotnet build RimObs.Library/RimObs.Library.csproj -c $(CONFIG) --nologo

build-collector: build-dashboard
	dotnet build RimObs.Collector/RimObs.Collector.csproj -c $(CONFIG) --nologo

build-dashboard:
	cd $(DASHBOARD_DIR) && pnpm build

deploy-collector:
	dotnet publish RimObs.Collector/RimObs.Collector.csproj \
		-c $(CONFIG) --use-current-runtime --no-self-contained \
		-p:PublishSingleFile=false \
		-o $(COLLECTOR_DEPLOY_DIR) --nologo
	printf '{"schema_version":1,"version":"0.0.0-dev","library_schema_compat":{"min":1,"max":1}}\n' > $(COLLECTOR_DEPLOY_DIR)/Collector.version

test:
	dotnet test $(SLN) -c $(CONFIG) --nologo --no-build

format:
	dotnet format $(SLN)
	cd $(DASHBOARD_DIR) && pnpm format || true

lint:
	dotnet format $(SLN) --verify-no-changes
	cd $(DASHBOARD_DIR) && pnpm lint

watch:
	dotnet watch --project RimObs.Collector run -- serve

publish-collector: build-dashboard
	@for rid in $(RIDS); do \
		echo "==> publish $$rid"; \
		dotnet publish RimObs.Collector/RimObs.Collector.csproj \
			-c Release -r $$rid --self-contained true \
			-p:PublishSingleFile=true \
			-o Collector/$$rid \
			--nologo; \
	done
