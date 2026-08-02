# The run image: a distro, whatever the cases need on top of it, and the two
# executables under test. Its build context holds only what is copied below,
# staged by run.sh.
ARG RUN_IMAGE=ubuntu:24.04

FROM ${RUN_IMAGE}

COPY prepare.sh /tmp/prepare.sh
RUN /tmp/prepare.sh && rm /tmp/prepare.sh

COPY github-token-broker github-token /usr/local/bin/

# Both binaries start on this run image. That is the floor claim.
RUN github-token version && ! github-token-broker
