#include "telemetry_security.h"

#include <stdbool.h>
#include <stdint.h>
#include <string.h>

// Платформенные функции должны быть реализованы в вашем проекте.
extern bool lte_is_connected(void);
extern bool send_packet_to_server(const tacid_packet_t* packet);
extern int64_t rtc_now_ms(void);

static tacid_packet_ring_t g_backlog;
static uint32_t g_sequence = 1;

void tacid_boot(void) {
    tacid_ring_init(&g_backlog);
}

void tacid_prepare_and_send(
    uint32_t beacon_id,
    uint8_t key_version,
    const uint8_t* key,
    uint32_t key_len,
    tacid_distance_t* distances,
    uint8_t count) {

    tacid_packet_t p;
    memset(&p, 0, sizeof(p));

    p.beacon_id = beacon_id;
    p.sequence = g_sequence++;
    p.timestamp_ms = rtc_now_ms();
    p.key_version = key_version;
    p.count = count;
    memcpy(p.distances, distances, sizeof(tacid_distance_t) * count);

    char canonical[512];
    uint32_t payload_len = tacid_build_canonical_payload(&p, canonical, sizeof(canonical));
    if (payload_len == 0) {
        return;
    }

    tacid_hmac_sha256(key, key_len, (const uint8_t*)canonical, payload_len, p.hmac);

    if (!lte_is_connected() || !send_packet_to_server(&p)) {
        // Если сеть упала, накапливаем пакет в кольцевом буфере.
        (void)tacid_ring_push(&g_backlog, &p);
    }
}

void tacid_flush_backlog(void) {
    if (!lte_is_connected()) {
        return;
    }

    // Отправляем накопленные пакеты в порядке FIFO.
    for (;;) {
        tacid_packet_t p;
        if (!tacid_ring_pop(&g_backlog, &p)) {
            break;
        }

        if (!send_packet_to_server(&p)) {
            // Если сеть снова пропала, возвращаем пакет в буфер и прекращаем flush.
            (void)tacid_ring_push(&g_backlog, &p);
            break;
        }
    }
}
