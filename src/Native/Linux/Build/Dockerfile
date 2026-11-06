FROM mcr.microsoft.com/dotnet/sdk:8.0
RUN apt-get update && apt-get install -y --no-install-recommends make jq clang llvm cmake ninja-build git curl unzip zip pkg-config autoconf automake libtool g++ libssl-dev python3 && rm -rf /var/lib/apt/lists/*
RUN git init /opt/vcpkg && git -C /opt/vcpkg remote add origin https://github.com/microsoft/vcpkg && git -C /opt/vcpkg fetch --depth 1 origin 2d318737bc7daa8a872f4469955a0559686f7580 && git -C /opt/vcpkg checkout FETCH_HEAD && /opt/vcpkg/bootstrap-vcpkg.sh -disableMetrics
ENV VCPKG_ROOT=/opt/vcpkg

# Upstream explicitly links the dynamic libdl.so name; glibc 2.34+ retains libdl.so.2.
RUN ln -s /lib/x86_64-linux-gnu/libdl.so.2 /usr/lib/x86_64-linux-gnu/libdl.so
